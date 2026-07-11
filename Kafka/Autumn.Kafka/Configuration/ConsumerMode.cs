namespace Autumn.Kafka.Configuration
{
    /// <summary>
    /// Controls how many Kafka consumers Autumn.Kafka runs per request topic.
    /// </summary>
    public enum ConsumerMode
    {
        /// <summary>
        /// One consumer per request topic (default). Messages on a topic are processed
        /// sequentially by a single consumer. Simplest and fully isolated: a slow handler
        /// on one topic never affects other topics. Scale throughput horizontally by running
        /// more application instances.
        /// </summary>
        Single,

        /// <summary>
        /// Auto-scale consumers per topic. Autumn.Kafka starts several consumers in the same
        /// consumer group for a topic — up to the topic's partition count (optionally capped by
        /// <see cref="AutumnKafkaOptions.MaxConsumersPerTopic"/>). Kafka's group protocol spreads
        /// partitions across them, giving in-process parallelism while preserving per-partition
        /// ordering. More consumers means more threads and broker connections.
        /// </summary>
        Auto
    }
}
