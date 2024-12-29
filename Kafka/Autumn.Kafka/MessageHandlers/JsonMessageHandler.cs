using System.Text;
using Autumn.Kafka.Exceptions.ConsumerExceptions;
using Autumn.Kafka.Exceptions.ProducerExceptions;
using Autumn.Kafka.Utils;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Autumn.Kafka.MessageHandlers
{
    public class JsonMessageHandler(
        KafkaProducer producer,
        IConsumer<string, string> consumer,
        TopicConfig requestTopicConfig,
        MessageHandlerConfig messageHandlerConfig,
        ILogger<JsonMessageHandler> logger,
        IServiceProvider serviceProvider)
        : MessageHandler
    {
        public override async Task Consume()
        {
            try
            {
                consumer.Assign(GetPartitions());
                while (true)
                {
                    try
                    {
                        ConsumeResult<string,string> message = consumer.Consume();
                        if(await HandleMessage(message))
                        {
                            consumer.Commit();
                        }
                        throw new HandleMethodException("0_0");
                    }
                    catch(Exception ex)
                    {
                        logger.LogError(ex, ex.Message);
                        consumer.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, ex.Message);
                consumer.Commit();
            }
        }
        private List<TopicPartition> GetPartitions()
        {
            var partitions = new List<TopicPartition>();
            for (int partition = 0; partition < requestTopicConfig.PartitionsCount; partition++)
            {
                partitions.Add(new TopicPartition(requestTopicConfig.TopicName, partition));
            }
            return partitions;
        }
        private async Task<bool> HandleMessage(ConsumeResult<string,string> message)
        {
            var headerBytes = message.Message.Headers
                        .FirstOrDefault(x => x.Key.Equals("method"));
            if (headerBytes != null)
            {
                var methodString = Encoding.UTF8.GetString(headerBytes.GetValueBytes());
                if(messageHandlerConfig.kafkaMethodExecutionConfigs.Any(x=>x.KafkaMethodName.Equals(methodString)))
                {
                    KafkaMethodExecutionConfig config = messageHandlerConfig.kafkaMethodExecutionConfigs.FirstOrDefault(x=>x.KafkaMethodName.Equals(methodString)) ?? throw new MethodInvalidException("Invalid method name");
                    object result;
                    if(config.ServiceMethodPair.Parameter!=null)
                    {
                        Type type =config.ServiceMethodPair.Parameter.GetType();
                        result = ServiceResolver.InvokeMethodByHeader(serviceProvider, config.ServiceMethodPair.Method, config.ServiceMethodPair.Service, JsonConvert.DeserializeObject(message.Message.Value, type));
                    }
                    else
                    {
                        result = ServiceResolver.InvokeMethodByHeader(serviceProvider, config.ServiceMethodPair.Method, config.ServiceMethodPair.Service, null);
                    }
                    if(config.RequireResponse)
                    {
                        if(config.KafkaServiceName!=null)
                        {
                            return await SendResponse(config,new Message<string, string>(){
                            Key = message.Message.Key,
                                Value = JsonConvert.SerializeObject(result),
                                Headers = [
                                    new Header("method",Encoding.UTF8.GetBytes(methodString)),
                                    new Header("sender",Encoding.UTF8.GetBytes(config.KafkaServiceName))
                                ]
                            });
                        }
                        return await SendResponse(config,new Message<string, string>(){
                            Key = message.Message.Key,
                                Value = JsonConvert.SerializeObject(result),
                                Headers = [
                                    new Header("method",Encoding.UTF8.GetBytes(methodString)),
                                    new Header("sender",Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("GLOBAL_SERVICE_NAME") ?? throw new ConfigInvalidException("Invalid config")))
                                ]
                            });
                    }
                    else
                    {
                        return true;
                    }
                }
                throw new MethodInvalidException("Invalid method name");
            }
            throw new HeaderBytesNullException("Header bytes are null");
        }
        private async Task<bool> SendResponse(KafkaMethodExecutionConfig config, Message<string,string> message)
        {
            try
            {
                if(config is { responseTopicPartition: not null, responseTopicConfig: not null })
                {
                    return await producer.ProduceAsync(config.responseTopicConfig, (int)config.responseTopicPartition, message);
                }
                throw new ProducerException("Failed to send response");
            }
            catch
            {
                return false;
            }
        }
    }
}