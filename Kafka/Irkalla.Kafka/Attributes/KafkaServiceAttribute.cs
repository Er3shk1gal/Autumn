namespace Irkalla.Kafka.Attributes
{
    /// <summary>
    /// Marks a class as a Kafka service that consumes messages from a topic.
    /// Methods within this class should be decorated with <see cref="KafkaMethodAttribute"/>.
    /// <para>
    /// Usage:
    /// <code>
    /// [KafkaService("orders-request", "order-service")]
    /// public class OrderKafkaService
    /// {
    ///     [KafkaMethod("CreateOrder", RequiresResponse = true)]
    ///     public OrderResult CreateOrder(CreateOrderRequest request) { ... }
    /// }
    /// </code>
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class KafkaServiceAttribute : Attribute
    {
        /// <summary>
        /// The Kafka topic to consume messages from.
        /// </summary>
        public string RequestTopic { get; }

        /// <summary>
        /// A logical name identifying this service in Kafka message headers.
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// Number of partitions for the request topic. Default is 1.
        /// </summary>
        public int RequestPartitions { get; set; } = 1;

        /// <summary>
        /// Replication factor for the request topic. Default is 1.
        /// </summary>
        public short RequestReplicationFactor { get; set; } = 1;

        /// <summary>
        /// The serialization format for messages. Default is JSON.
        /// </summary>
        public MessageHandlerType HandlerType { get; set; } = MessageHandlerType.JSON;

        /// <summary>
        /// The Kafka topic to send responses to. If null, responses are not sent at service level.
        /// Individual methods can still require responses via <see cref="KafkaMethodAttribute.RequiresResponse"/>.
        /// </summary>
        public string? ResponseTopic { get; set; }

        /// <summary>
        /// Number of partitions for the response topic. Default is 1.
        /// </summary>
        public int ResponsePartitions { get; set; } = 1;

        /// <summary>
        /// Replication factor for the response topic. Default is 1.
        /// </summary>
        public short ResponseReplicationFactor { get; set; } = 1;

        /// <summary>
        /// Default response partition. Methods can override this via <see cref="KafkaMethodAttribute.ResponsePartition"/>.
        /// </summary>
        public int DefaultResponsePartition { get; set; } = 0;

        public KafkaServiceAttribute(string requestTopic, string serviceName)
        {
            RequestTopic = requestTopic;
            ServiceName = serviceName;
        }
    }
}
