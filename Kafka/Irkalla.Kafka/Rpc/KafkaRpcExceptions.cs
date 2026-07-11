using Irkalla.Kafka.Exceptions;

namespace Irkalla.Kafka.Rpc
{
    /// <summary>An RPC call did not receive a reply within its timeout.</summary>
    public class KafkaRpcTimeoutException : KafkaException
    {
        public KafkaRpcTimeoutException(string message) : base(message) { }
    }

    /// <summary>The RPC client was shut down while calls were still in flight.</summary>
    public class KafkaRpcClientClosedException : KafkaException
    {
        public KafkaRpcClientClosedException(string message) : base(message) { }
    }
}
