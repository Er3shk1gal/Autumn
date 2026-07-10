using System.Text;
using Autumn.Kafka.Exceptions;
using Autumn.Kafka.Utils;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Autumn.Kafka.MessageHandlers
{
    /// <summary>
    /// Message handler that deserializes JSON messages, routes them by "method" header
    /// to the appropriate service method, and optionally sends JSON responses.
    /// </summary>
    public class JsonMessageHandler(
        KafkaProducer producer,
        IConsumer<string, string> consumer,
        MessageHandlerConfig messageHandlerConfig,
        ILogger<JsonMessageHandler> logger,
        IServiceProvider serviceProvider)
        : MessageHandler
    {
        public override async Task Consume(CancellationToken cancellationToken = default)
        {
            try
            {
                consumer.Assign(GetPartitions());
                logger.LogInformation("Consumer assigned to topic '{Topic}' with {Count} partition(s)",
                    messageHandlerConfig.RequestTopicConfig.TopicName,
                    messageHandlerConfig.RequestTopicConfig.PartitionsCount);

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = consumer.Consume(cancellationToken);
                        if (consumeResult == null || consumeResult.IsPartitionEOF)
                        {
                            continue;
                        }

                        if (await HandleMessage(consumeResult))
                        {
                            consumer.Commit(consumeResult);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ConsumeException ex)
                    {
                        logger.LogError(ex, "Kafka consume error on topic '{Topic}'",
                            messageHandlerConfig.RequestTopicConfig.TopicName);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing message from topic '{Topic}'",
                            messageHandlerConfig.RequestTopicConfig.TopicName);
                    }
                }
            }
            finally
            {
                consumer.Close();
                logger.LogInformation("Consumer closed for topic '{Topic}'",
                    messageHandlerConfig.RequestTopicConfig.TopicName);
            }
        }

        private List<TopicPartition> GetPartitions()
        {
            var partitions = new List<TopicPartition>();
            for (int i = 0; i < messageHandlerConfig.RequestTopicConfig.PartitionsCount; i++)
            {
                partitions.Add(new TopicPartition(
                    messageHandlerConfig.RequestTopicConfig.TopicName, i));
            }
            return partitions;
        }

        private async Task<bool> HandleMessage(ConsumeResult<string, string> message)
        {
            var headerBytes = message.Message.Headers
                .FirstOrDefault(x => x.Key.Equals("method"));

            if (headerBytes == null)
            {
                throw new KafkaConsumerException("Message is missing required 'method' header");
            }

            var methodName = Encoding.UTF8.GetString(headerBytes.GetValueBytes());

            var config = messageHandlerConfig.KafkaMethodExecutionConfigs
                .FirstOrDefault(x => x.KafkaMethodName.Equals(methodName));

            if (config == null)
            {
                throw new KafkaConsumerException($"No handler registered for method '{methodName}'");
            }

            var result = InvokeServiceMethod(config, message.Message.Value);

            if (!config.RequireResponse)
            {
                return true;
            }

            var senderName = config.KafkaServiceName
                ?? throw new KafkaConfigurationException(
                    "KafkaServiceName is not configured for response handling");

            var responseMessage = new Message<string, string>
            {
                Key = message.Message.Key,
                Value = JsonConvert.SerializeObject(result),
                Headers =
                [
                    new Header("method", Encoding.UTF8.GetBytes(methodName)),
                    new Header("sender", Encoding.UTF8.GetBytes(senderName))
                ]
            };

            return await SendResponse(config, responseMessage);
        }

        private object InvokeServiceMethod(KafkaMethodExecutionConfig config, string messageValue)
        {
            if (config.ServiceMethodPair.Parameters != null && config.ServiceMethodPair.Parameters.Any())
            {
                var parameters = new List<object>();
                foreach (var paramInfo in config.ServiceMethodPair.Parameters)
                {
                    var deserialized = JsonConvert.DeserializeObject(messageValue, paramInfo.ParameterType);
                    if (deserialized != null)
                    {
                        parameters.Add(deserialized);
                    }
                }

                return ServiceResolver.InvokeMethod(
                    serviceProvider,
                    config.ServiceMethodPair.Method,
                    config.ServiceMethodPair.Service,
                    parameters);
            }

            return ServiceResolver.InvokeMethod(
                serviceProvider,
                config.ServiceMethodPair.Method,
                config.ServiceMethodPair.Service,
                null);
        }

        private async Task<bool> SendResponse(
            KafkaMethodExecutionConfig config,
            Message<string, string> message)
        {
            if (config.ResponseTopicConfig == null || config.ResponseTopicPartition == null)
            {
                throw new KafkaConfigurationException(
                    $"Response topic is not configured for method '{config.KafkaMethodName}'. " +
                    "Set ResponseTopic on [KafkaService] attribute.");
            }

            return await producer.ProduceAsync(
                config.ResponseTopicConfig,
                (int)config.ResponseTopicPartition,
                message);
        }
    }
}