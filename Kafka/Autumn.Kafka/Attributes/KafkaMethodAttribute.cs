namespace Autumn.Kafka.Attributes
{
    /// <summary>
    /// Marks a method as a Kafka message handler within a <see cref="KafkaServiceAttribute"/> class.
    /// The method is invoked when a message with a matching "method" header is consumed.
    /// <para>
    /// Usage:
    /// <code>
    /// [KafkaMethod("CreateOrder", RequiresResponse = true)]
    /// public OrderResult CreateOrder(CreateOrderRequest request) { ... }
    /// </code>
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class KafkaMethodAttribute : Attribute
    {
        /// <summary>
        /// The name used to identify this method in Kafka message "method" headers.
        /// </summary>
        public string MethodName { get; }

        /// <summary>
        /// The partition this method listens on. Default is 0.
        /// Only relevant for multi-partition topics.
        /// </summary>
        public int Partition { get; set; } = 0;

        /// <summary>
        /// Whether this method should send a response back after processing.
        /// </summary>
        public bool RequiresResponse { get; set; } = false;

        /// <summary>
        /// The partition to send the response to. If null, uses the service-level default.
        /// </summary>
        public int ResponsePartition { get; set; } = -1;

        public KafkaMethodAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}
