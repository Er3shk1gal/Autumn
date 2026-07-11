namespace Irkalla.Kafka.Deduplication
{
    /// <summary>
    /// Optional consumer-side de-duplication. If an <see cref="IMessageDeduplicator"/> is registered
    /// in DI, Irkalla checks each message's <c>message-id</c> header before invoking the handler and
    /// records it after successful processing — so a redelivered message is not processed twice.
    /// <para>
    /// The framework does not ship a durable store; implement this over Redis, a database, etc. An
    /// <c>InMemoryMessageDeduplicator</c> is provided for single-instance/testing use. Messages
    /// without a <c>message-id</c> header are never de-duplicated (set it when producing).
    /// </para>
    /// </summary>
    public interface IMessageDeduplicator
    {
        /// <summary>Returns true if <paramref name="messageId"/> was already processed (skip it).</summary>
        Task<bool> IsDuplicateAsync(string messageId, string topic, CancellationToken cancellationToken = default);

        /// <summary>Records that <paramref name="messageId"/> was processed successfully.</summary>
        Task MarkProcessedAsync(string messageId, string topic, CancellationToken cancellationToken = default);
    }
}
