using Confluent.Kafka;

namespace Autumn.Kafka.Configuration
{
    /// <summary>
    /// Configuration options for Autumn.Kafka.
    /// <para>
    /// Usage in Startup/Program.cs:
    /// <code>
    /// services.AddAutumnKafka(options =>
    /// {
    ///     options.BootstrapServers = "localhost:9092";
    ///     options.GroupId = "my-consumer-group";
    ///     options.ServiceName = "my-service";
    /// });
    /// </code>
    /// </para>
    /// </summary>
    public class AutumnKafkaOptions
    {
        /// <summary>
        /// Kafka bootstrap servers (comma-separated). Default: "localhost:9092".
        /// </summary>
        public string BootstrapServers { get; set; } = "localhost:9092";

        /// <summary>
        /// Consumer group ID. Required.
        /// </summary>
        public string GroupId { get; set; } = null!;

        /// <summary>
        /// Logical name of this service, used in response message "sender" headers.
        /// Falls back to the service-level attribute name if not set globally.
        /// </summary>
        public string? ServiceName { get; set; }

        /// <summary>
        /// Whether to automatically create topics that don't exist. Default: true.
        /// </summary>
        public bool AutoCreateTopics { get; set; } = true;

        /// <summary>
        /// Auto offset reset strategy for consumers. Default: Earliest.
        /// </summary>
        public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Earliest;

        /// <summary>
        /// Whether to enable auto-commit. Default: false (manual commit after processing).
        /// </summary>
        public bool EnableAutoCommit { get; set; } = false;

        /// <summary>
        /// The assembly to scan for <see cref="Attributes.KafkaServiceAttribute"/> classes.
        /// If null, the entry assembly is scanned.
        /// </summary>
        public System.Reflection.Assembly? ServiceAssembly { get; set; }

        /// <summary>
        /// Optional: override consumer configuration. Applied after standard settings.
        /// </summary>
        public Action<ConsumerConfig>? ConfigureConsumer { get; set; }

        /// <summary>
        /// Optional: override producer configuration. Applied after standard settings.
        /// </summary>
        public Action<ProducerConfig>? ConfigureProducer { get; set; }

        internal ConsumerConfig BuildConsumerConfig()
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = BootstrapServers,
                GroupId = GroupId,
                AutoOffsetReset = AutoOffsetReset,
                EnableAutoCommit = EnableAutoCommit,
            };

            ConfigureConsumer?.Invoke(config);
            return config;
        }

        internal ProducerConfig BuildProducerConfig()
        {
            var config = new ProducerConfig
            {
                BootstrapServers = BootstrapServers,
            };

            ConfigureProducer?.Invoke(config);
            return config;
        }

        internal AdminClientConfig BuildAdminClientConfig()
        {
            return new AdminClientConfig
            {
                BootstrapServers = BootstrapServers,
            };
        }
    }
}
