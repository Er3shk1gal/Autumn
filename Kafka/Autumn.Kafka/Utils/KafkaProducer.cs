using Autumn.Kafka.Exceptions;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Autumn.Kafka.Utils
{
    /// <summary>
    /// Wraps <see cref="IProducer{TKey,TValue}"/> with topic auto-creation
    /// and structured error handling.
    /// </summary>
    public class KafkaProducer
    {
        private readonly IProducer<string, string> _producer;
        private readonly ILogger<KafkaProducer> _logger;
        private readonly KafkaTopicManager _kafkaTopicManager;

        public KafkaProducer(
            IProducer<string, string> producer,
            ILogger<KafkaProducer> logger,
            KafkaTopicManager kafkaTopicManager)
        {
            _producer = producer;
            _logger = logger;
            _kafkaTopicManager = kafkaTopicManager;
        }

        /// <summary>
        /// Produces a message to the specified topic and partition.
        /// Creates the topic automatically if it does not exist.
        /// </summary>
        public async Task<bool> ProduceAsync(
            TopicConfig topic, int partition, Message<string, string> message)
        {
            try
            {
                await EnsureTopicAvailableAsync(topic, partition);

                var deliveryResult = await _producer.ProduceAsync(
                    new TopicPartition(topic.TopicName, new Partition(partition)),
                    message);

                if (deliveryResult.Status == PersistenceStatus.Persisted)
                {
                    _logger.LogDebug(
                        "Message delivered to {Topic}[{Partition}] at offset {Offset}",
                        topic.TopicName, partition, deliveryResult.Offset);
                    return true;
                }

                _logger.LogError(
                    "Message not persisted: status={Status}, topic={Topic}[{Partition}]",
                    deliveryResult.Status, topic.TopicName, partition);

                throw new KafkaProducerException(
                    $"Message delivery status: {deliveryResult.Status}");
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Kafka produce error on {Topic}[{Partition}]",
                    topic.TopicName, partition);
                throw new KafkaProducerException("Error producing message", ex);
            }
        }

        private async Task EnsureTopicAvailableAsync(TopicConfig topic, int partition)
        {
            if (_kafkaTopicManager.CheckTopicContainsPartitions(topic.TopicName, partition))
            {
                return;
            }

            _logger.LogInformation(
                "Topic '{Topic}' missing partition {Partition}, auto-creating...",
                topic.TopicName, partition);

            await _kafkaTopicManager.CreateTopicAsync(
                topic.TopicName, topic.PartitionsCount, topic.ReplicationFactor);
        }
    }
}