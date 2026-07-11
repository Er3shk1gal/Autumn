namespace Irkalla.Kafka.Producing
{
    /// <summary>
    /// Sends messages to Irkalla.Kafka services (or any topic) with the routing <c>method</c> header
    /// and JSON serialization, without hand-building Kafka messages. Registered automatically by
    /// <c>AddIrkallaKafka</c> and by <c>AddIrkallaKafkaProducer</c> (producer-only apps).
    /// </summary>
    public interface IKafkaProducer
    {
        /// <summary>
        /// Serializes <paramref name="payload"/> as JSON and produces it to <paramref name="topic"/>
        /// with a <c>method</c> header equal to <paramref name="method"/>, so an Irkalla service on
        /// that topic routes it to the matching <c>[KafkaMethod]</c>.
        /// </summary>
        /// <param name="topic">Destination topic (a service's request topic).</param>
        /// <param name="method">Value of the <c>method</c> header — the target <c>[KafkaMethod]</c> name.</param>
        /// <param name="payload">The message body; serialized with the configured JSON options.</param>
        /// <param name="key">Optional Kafka message key (controls partitioning / ordering).</param>
        /// <param name="correlationId">Optional value for a <c>correlation-id</c> header.</param>
        /// <param name="messageId">Optional value for a <c>message-id</c> header (consumer-side dedup).</param>
        /// <param name="headers">Optional extra headers.</param>
        /// <param name="cancellationToken">Cancels the produce operation.</param>
        Task SendAsync<T>(
            string topic,
            string method,
            T payload,
            string? key = null,
            string? correlationId = null,
            string? messageId = null,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default);
    }
}
