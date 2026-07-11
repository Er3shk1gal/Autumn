using System.Text;
using System.Text.Json;
using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Exceptions;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Irkalla.Kafka.Producing
{
    /// <summary>
    /// Default <see cref="IKafkaProducer"/> — JSON serialization over the shared singleton
    /// <see cref="IProducer{TKey,TValue}"/>. Thread-safe.
    /// </summary>
    public class KafkaMessageProducer(
        IProducer<string, byte[]> producer,
        IrkallaKafkaOptions options,
        ILogger<KafkaMessageProducer> logger)
        : IKafkaProducer
    {
        public async Task SendAsync<T>(
            string topic,
            string method,
            T payload,
            string? key = null,
            string? correlationId = null,
            string? messageId = null,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic is required.", nameof(topic));
            if (string.IsNullOrWhiteSpace(method))
                throw new ArgumentException("Method is required.", nameof(method));

            var value = JsonSerializer.SerializeToUtf8Bytes(payload, options.JsonSerializerOptions);

            var kafkaHeaders = new Headers { { "method", Encoding.UTF8.GetBytes(method) } };
            if (!string.IsNullOrEmpty(options.ServiceName))
                kafkaHeaders.Add("sender", Encoding.UTF8.GetBytes(options.ServiceName));
            if (!string.IsNullOrEmpty(correlationId))
                kafkaHeaders.Add("correlation-id", Encoding.UTF8.GetBytes(correlationId));
            if (!string.IsNullOrEmpty(messageId))
                kafkaHeaders.Add("message-id", Encoding.UTF8.GetBytes(messageId));
            if (headers != null)
                foreach (var kv in headers)
                    kafkaHeaders.Add(kv.Key, Encoding.UTF8.GetBytes(kv.Value));

            var message = new Message<string, byte[]> { Key = key!, Value = value, Headers = kafkaHeaders };

            try
            {
                var result = await producer.ProduceAsync(topic, message, cancellationToken);
                if (result.Status == PersistenceStatus.NotPersisted)
                {
                    throw new KafkaProducerException(
                        $"Message to '{topic}' (method '{method}') was not persisted.");
                }
                logger.LogDebug("Sent '{Method}' to {Topic}[{Partition}]@{Offset}",
                    method, topic, result.Partition.Value, result.Offset);
            }
            catch (ProduceException<string, byte[]> ex)
            {
                logger.LogError(ex, "Failed to send '{Method}' to '{Topic}'", method, topic);
                throw new KafkaProducerException($"Failed to send message to '{topic}'.", ex);
            }
        }
    }
}
