namespace Irkalla.Kafka.Rpc
{
    /// <summary>
    /// Request/reply over Kafka: sends a request to a service's topic and awaits the typed reply,
    /// matched by a <c>correlation-id</c> header. JSON serialization. Register with
    /// <c>AddIrkallaKafkaRpcClient</c>.
    /// </summary>
    public interface IKafkaRpcClient
    {
        /// <summary>
        /// Sends <paramref name="request"/> to <paramref name="topic"/> with the given
        /// <paramref name="method"/> and awaits the reply, deserialized to
        /// <typeparamref name="TResponse"/>.
        /// </summary>
        /// <exception cref="KafkaRpcTimeoutException">No reply arrived within the timeout.</exception>
        /// <exception cref="KafkaRpcClientClosedException">The client shut down while the call was in flight.</exception>
        Task<TResponse?> CallAsync<TRequest, TResponse>(
            string topic,
            string method,
            TRequest request,
            TimeSpan? timeout = null,
            string? key = null,
            CancellationToken cancellationToken = default);
    }
}
