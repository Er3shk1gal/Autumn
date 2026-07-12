using System.Diagnostics;
using Irkalla.Kafka.Exceptions;
using Irkalla.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Irkalla.Kafka.Utils
{
    /// <summary>
    /// Wraps <see cref="IProducer{TKey,TValue}"/> with topic auto-creation
    /// and structured error handling.
    /// </summary>
    public class KafkaProducer(
        IProducer<string, byte[]> producer,
        ILogger<KafkaProducer> logger,
        KafkaTopicManager kafkaTopicManager)
    {
        public async Task<bool> ProduceAsync(
            TopicConfig topic, int partition, Message<string, byte[]> message)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await EnsureTopicAvailableAsync(topic, partition);

                var target = partition < 0
                    ? new TopicPartition(topic.TopicName, Partition.Any)
                    : new TopicPartition(topic.TopicName, new Partition(partition));

                var deliveryResult = await producer.ProduceAsync(
                    target,
                    message);

                if (deliveryResult.Status == PersistenceStatus.Persisted)
                {
                    KafkaTelemetry.RecordProduced(topic.TopicName);
                    logger.LogDebug(
                        "Message delivered to {Topic}[{Partition}] at offset {Offset}",
                        topic.TopicName, deliveryResult.Partition.Value, deliveryResult.Offset);
                    return true;
                }

                KafkaTelemetry.RecordProduceFailed(topic.TopicName);
                logger.LogError(
                    "Message not persisted: status={Status}, topic={Topic}[{Partition}]",
                    deliveryResult.Status, topic.TopicName, deliveryResult.Partition.Value);

                throw new KafkaProducerException(
                    $"Message delivery status: {deliveryResult.Status}");
            }
            catch (ProduceException<string, byte[]> ex)
            {
                KafkaTelemetry.RecordProduceFailed(topic.TopicName);
                logger.LogError(ex, "Kafka produce error on {Topic}[{Partition}]",
                    topic.TopicName, partition);
                throw new KafkaProducerException("Error producing message", ex);
            }
            finally
            {
                KafkaTelemetry.RecordProduceDuration(stopwatch.Elapsed.TotalMilliseconds, topic.TopicName);
            }
        }

        private async Task EnsureTopicAvailableAsync(TopicConfig topic, int partition)
        {
            var exists = partition < 0
                ? kafkaTopicManager.CheckTopicExists(topic.TopicName)
                : kafkaTopicManager.CheckTopicContainsPartitions(topic.TopicName, partition);

            if (exists)
            {
                return;
            }

            logger.LogInformation(
                "Topic '{Topic}' missing partition {Partition}, auto-creating...",
                topic.TopicName, partition);

            await kafkaTopicManager.CreateTopicAsync(
                topic.TopicName, topic.PartitionsCount, topic.ReplicationFactor);
        }
    }
}