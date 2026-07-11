using Autumn.Kafka.Configuration;
using Autumn.Kafka.Utils.Models;
using Autumn.Kafka.Utils;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autumn.Kafka.Hosting
{
    /// <summary>
    /// Background service for a single Kafka topic consumer.
    /// One instance is registered per unique request topic discovered during assembly scanning.
    /// Each instance has its own <see cref="IConsumer{TKey,TValue}"/> and lifecycle.
    /// </summary>
    public class KafkaConsumerHostedService(
        MessageHandlerConfig handlerConfig,
        IServiceProvider serviceProvider,
        ILogger<KafkaConsumerHostedService> logger)
        : BackgroundService
    {

        private MessageHandlers.BaseMessageHandler? _handler;

        /// <summary>
        /// The topic this consumer is bound to. Useful for diagnostics.
        /// </summary>
        public string TopicName => handlerConfig.RequestTopicConfig.TopicName;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var topicName = handlerConfig.RequestTopicConfig.TopicName;

            logger.LogInformation(
                "Autumn.Kafka: Starting consumer for topic '{Topic}' ({MethodCount} method(s))",
                topicName, handlerConfig.KafkaMethodExecutionConfigs.Count);

            var options = serviceProvider.GetRequiredService<AutumnKafkaOptions>();
            var topicManager = serviceProvider.GetRequiredService<KafkaTopicManager>();

            if (options.AutoCreateTopics)
            {
                await topicManager.CreateTopicAsync(
                    topicName,
                    handlerConfig.RequestTopicConfig.PartitionsCount,
                    handlerConfig.RequestTopicConfig.ReplicationFactor);
            }
            else
            {
                if (!topicManager.CheckTopicExists(topicName))
                {
                    throw new Exceptions.KafkaConfigurationException(
                        $"Request topic '{topicName}' does not exist and AutoCreateTopics is false.");
                }
            }

            _handler = CreateHandler();

            try
            {
                // Dedicated long-running thread: the blocking Consume loop must NOT run on a
                // ThreadPool thread, or N topics (× Auto-mode consumers) would pin the pool and
                // starve async continuations. Awaited so ExecuteAsync stays alive for the
                // consumer's lifetime — the host waits for a graceful shutdown, and fatal errors
                // (ErrorPolicy.Stop) still propagate.
                await Task.Factory.StartNew(
                    () => _handler.Consume(stoppingToken),
                    stoppingToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation(
                    "Autumn.Kafka: Consumer for topic '{Topic}' stopped.", topicName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Autumn.Kafka: Fatal error in consumer for topic '{Topic}'", topicName);
                throw;
            }
        }

        private MessageHandlers.BaseMessageHandler CreateHandler()
        {
            var consumer = serviceProvider.GetRequiredService<Func<IConsumer<string, byte[]>>>()();
            var producer = serviceProvider.GetRequiredService<KafkaProducer>();
            var options = serviceProvider.GetRequiredService<AutumnKafkaOptions>();
            
            return handlerConfig.HandlerType switch
            {
                Attributes.MessageHandlerType.AVRO => new MessageHandlers.AvroMessageHandler(
                    producer, consumer, handlerConfig, 
                    serviceProvider.GetRequiredService<ILogger<MessageHandlers.AvroMessageHandler>>(),
                    serviceProvider,
                    serviceProvider.GetRequiredService<Confluent.SchemaRegistry.ISchemaRegistryClient>(),
                    options),
                    
                Attributes.MessageHandlerType.PROTOBUF => new MessageHandlers.ProtobufMessageHandler(
                    producer, consumer, handlerConfig,
                    serviceProvider.GetRequiredService<ILogger<MessageHandlers.ProtobufMessageHandler>>(),
                    serviceProvider,
                    serviceProvider.GetRequiredService<Confluent.SchemaRegistry.ISchemaRegistryClient>(),
                    options),
                    
                _ => new MessageHandlers.JsonMessageHandler(
                    producer, consumer, handlerConfig,
                    serviceProvider.GetRequiredService<ILogger<MessageHandlers.JsonMessageHandler>>(),
                    serviceProvider,
                    options)
            };
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation(
                "Autumn.Kafka: Shutting down consumer for topic '{Topic}'", TopicName);
            return base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _handler?.Dispose();
            base.Dispose();
        }
    }
}
