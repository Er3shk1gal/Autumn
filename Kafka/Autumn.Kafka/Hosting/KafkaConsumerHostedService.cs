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
    public class KafkaConsumerHostedService : BackgroundService
    {
        private readonly MessageHandlerConfig _handlerConfig;
        private readonly IServiceProvider _serviceProvider;
        private readonly AutumnKafkaOptions _options;
        private readonly ILogger<KafkaConsumerHostedService> _logger;

        public KafkaConsumerHostedService(
            MessageHandlerConfig handlerConfig,
            IServiceProvider serviceProvider,
            AutumnKafkaOptions options,
            ILogger<KafkaConsumerHostedService> logger)
        {
            _handlerConfig = handlerConfig;
            _serviceProvider = serviceProvider;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// The topic this consumer is bound to. Useful for diagnostics.
        /// </summary>
        public string TopicName => _handlerConfig.RequestTopicConfig.TopicName;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var topicName = _handlerConfig.RequestTopicConfig.TopicName;

            _logger.LogInformation(
                "Autumn.Kafka: Starting consumer for topic '{Topic}' ({MethodCount} method(s))",
                topicName, _handlerConfig.KafkaMethodExecutionConfigs.Count);

            try
            {
                var handler = CreateHandler();
                await handler.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "Autumn.Kafka: Consumer for topic '{Topic}' stopping...", topicName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Autumn.Kafka: Fatal error in consumer for topic '{Topic}'", topicName);
                throw;
            }
        }

        private MessageHandlers.MessageHandler CreateHandler()
        {
            // Each consumer gets its own IConsumer instance via transient resolution
            var consumer = _serviceProvider.GetRequiredService<IConsumer<string, string>>();
            var producer = _serviceProvider.GetRequiredService<KafkaProducer>();
            var logger = _serviceProvider.GetRequiredService<ILogger<MessageHandlers.JsonMessageHandler>>();

            return new MessageHandlers.JsonMessageHandler(
                producer, consumer, _handlerConfig, logger, _serviceProvider);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Autumn.Kafka: Shutting down consumer for topic '{Topic}'", TopicName);
            return base.StopAsync(cancellationToken);
        }
    }
}
