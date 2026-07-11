namespace Irkalla.Kafka.Rpc
{
    /// <summary>Options for the request/reply RPC client.</summary>
    public class KafkaRpcOptions
    {
        /// <summary>
        /// Topic the client's replies are delivered to. Default: <c>{GroupId}.replies</c>. Shared by
        /// all instances of the calling app; each instance filters replies by correlation id.
        /// </summary>
        public string? ReplyTopic { get; set; }

        /// <summary>Partitions to create the reply topic with (if it doesn't exist). Default: 1.</summary>
        public int ReplyTopicPartitions { get; set; } = 1;

        /// <summary>Default per-call timeout when none is passed to <c>CallAsync</c>. Default: 30s.</summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Maximum concurrent in-flight calls (back-pressure + pending-map bound). Default: 1024.</summary>
        public int MaxInFlightCalls { get; set; } = 1024;

        /// <summary>How often the deadline sweeper checks for timed-out calls. Default: 250ms.</summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    }
}
