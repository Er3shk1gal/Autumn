using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Utils.Models;
using Irkalla.Kafka.Utils;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Irkalla.Kafka.Hosting
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
        private readonly string _healthId = Guid.NewGuid().ToString("N");

        /// <summary>
        /// The topic this consumer is bound to. Useful for diagnostics.
        /// </summary>
        public string TopicName => handlerConfig.RequestTopicConfig.TopicName;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var topicName = handlerConfig.RequestTopicConfig.TopicName;

            logger.LogInformation(
                "Irkalla.Kafka: Starting consumer for topic '{Topic}' ({MethodCount} method(s))",
                topicName, handlerConfig.KafkaMethodExecutionConfigs.Count);

            var options = serviceProvider.GetRequiredService<IrkallaKafkaOptions>();
            var topicManager = serviceProvider.GetRequiredService<KafkaTopicManager>();
            var health = serviceProvider.GetRequiredService<ConsumerHealthState>();
            health.Report(_healthId, topicName, ConsumerStatus.Starting);

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
            health.Report(_healthId, topicName, ConsumerStatus.Running);

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
                health.Report(_healthId, topicName, ConsumerStatus.Stopped);
                logger.LogInformation(
                    "Irkalla.Kafka: Consumer for topic '{Topic}' stopped.", topicName);
            }
            catch (Exception ex)
            {
                health.Report(_healthId, topicName, ConsumerStatus.Faulted, ex.Message);
                logger.LogError(ex,
                    "Irkalla.Kafka: Fatal error in consumer for topic '{Topic}'", topicName);
                throw;
            }
        }

        private MessageHandlers.BaseMessageHandler CreateHandler()
        {
            var consumer = serviceProvider.GetRequiredService<Func<string?, IConsumer<string, byte[]>>>()(handlerConfig.GroupId);
            var producer = serviceProvider.GetRequiredService<KafkaProducer>();
            var options = serviceProvider.GetRequiredService<IrkallaKafkaOptions>();
            
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
                "Irkalla.Kafka: Shutting down consumer for topic '{Topic}'", TopicName);
            return base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _handler?.Dispose();
            base.Dispose();
        }
    }
}
