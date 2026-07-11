using Confluent.Kafka;

namespace Irkalla.Kafka.Configuration
{
    /// <summary>
    /// Configuration options for Irkalla.Kafka.
    /// <para>
    /// Usage in Startup/Program.cs:
    /// <code>
    /// services.AddIrkallaKafka(options =>
    /// {
    ///     options.BootstrapServers = "localhost:9092";
    ///     options.GroupId = "my-consumer-group";
    ///     options.ServiceName = "my-service";
    /// });
    /// </code>
    /// </para>
    /// </summary>
    public class IrkallaKafkaOptions
    {
        /// <summary>
        /// Default configuration section name bound by <c>AddIrkallaKafka(IConfiguration)</c>.
        /// </summary>
        public const string SectionName = "IrkallaKafka";

        /// <summary>
        /// Kafka bootstrap servers (comma-separated). Default: "localhost:9092".
        /// </summary>
        public string BootstrapServers { get; set; } = "localhost:9092";

        /// <summary>
        /// URL for Confluent Schema Registry. Required for Avro and Protobuf handlers.
        /// </summary>
        public string? SchemaRegistryUrl { get; set; }

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
        /// Defines how poison messages are handled. Default: ErrorPolicy.Dlq.
        /// </summary>
        public ErrorPolicy ErrorPolicy { get; set; } = ErrorPolicy.Dlq;

        /// <summary>
        /// Maximum number of retries for a poison message before applying the ErrorPolicy. Default: 3.
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Base delay between retries of a failed message. Grows exponentially
        /// (delay * 2^attempt). Default: 1 second.
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Upper bound on a single retry back-off delay. Caps the exponential growth of
        /// <see cref="RetryDelay"/> so a large <see cref="MaxRetries"/> cannot block the consumer
        /// poll loop long enough to trigger a max.poll.interval.ms group eviction. Default: 30 seconds.
        /// </summary>
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Enables the idempotent producer (<c>enable.idempotence</c>): librdkafka de-duplicates its
        /// own internal retries so a produce is never written twice to a partition. Default: true.
        /// This does NOT prevent duplicate <em>processing</em> on the consumer — for that use an
        /// <c>IMessageDeduplicator</c> or idempotent handlers.
        /// </summary>
        public bool EnableIdempotence { get; set; } = true;

        /// <summary>
        /// How many consumers to run per request topic. Default: <see cref="ConsumerMode.Single"/>.
        /// </summary>
        public ConsumerMode ConsumerMode { get; set; } = ConsumerMode.Single;

        /// <summary>
        /// In <see cref="ConsumerMode.Auto"/>, the maximum number of consumers to start per topic.
        /// 0 (default) means "use the topic's partition count". Ignored in <see cref="ConsumerMode.Single"/>.
        /// The effective count is never more than the partition count (extra consumers would sit idle).
        /// </summary>
        public int MaxConsumersPerTopic { get; set; } = 0;

        /// <summary>
        /// Suffix appended to the request topic name to form the DLQ topic name. Default: ".dlq".
        /// </summary>
        public string DlqTopicSuffix { get; set; } = ".dlq";

        /// <summary>
        /// Whether to include the exception stack trace in the <c>stacktrace</c> header of DLQ
        /// messages. Off by default: the stack trace can leak internal details (paths, types,
        /// versions) to anyone with read access to the DLQ topic. The <c>error</c> header (exception
        /// message) is always included. Enable only when the DLQ topic's access is trusted.
        /// </summary>
        public bool IncludeStackTraceInDlq { get; set; } = false;

        /// <summary>
        /// Auto offset reset strategy for consumers. Default: Earliest.
        /// </summary>
        public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Earliest;

        /// <summary>
        /// JSON serialization options used by the JSON message handler. Default: JsonSerializerDefaults.Web.
        /// </summary>
        public System.Text.Json.JsonSerializerOptions JsonSerializerOptions { get; set; } = new(System.Text.Json.JsonSerializerDefaults.Web);

        /// <summary>
        /// The assembly to scan for <see cref="Attributes.KafkaServiceAttribute"/> classes.
        /// If null (and <see cref="ServiceAssemblies"/> is empty), the entry assembly is scanned.
        /// </summary>
        public System.Reflection.Assembly? ServiceAssembly { get; set; }

        /// <summary>
        /// Multiple assemblies to scan for <see cref="Attributes.KafkaServiceAttribute"/> classes.
        /// When set (non-empty), takes precedence over <see cref="ServiceAssembly"/>; services from
        /// all assemblies are merged (services sharing a request topic share a consumer).
        /// </summary>
        public System.Reflection.Assembly[]? ServiceAssemblies { get; set; }

        /// <summary>
        /// Optional: override consumer configuration. Applied after standard settings.
        /// </summary>
        public Action<ConsumerConfig>? ConfigureConsumer { get; set; }

        /// <summary>
        /// Optional: override producer configuration. Applied after standard settings.
        /// </summary>
        public Action<ProducerConfig>? ConfigureProducer { get; set; }

        /// <summary>
        /// First-class SSL/TLS and SASL settings, applied to consumer, producer AND admin clients.
        /// The TLS work is done by librdkafka; Irkalla.Kafka only forwards the settings.
        /// </summary>
        public KafkaSecurityOptions Security { get; set; } = new();

        /// <summary>
        /// Raw librdkafka key/value settings (escape hatch) applied to consumer, producer and admin
        /// configs — covers any property not surfaced as a typed option. Applied after the typed
        /// settings and <see cref="Security"/>, but before the <c>Configure*</c> callbacks, so a
        /// callback can still override. No librdkafka setting is ever out of reach.
        /// </summary>
        public Dictionary<string, string> RawConfig { get; set; } = new();

        /// <summary>Optional: override admin client configuration. Applied last.</summary>
        public Action<AdminClientConfig>? ConfigureAdminClient { get; set; }

        /// <summary>Optional: override Schema Registry configuration (e.g. basic auth, SSL). Applied last.</summary>
        public Action<Confluent.SchemaRegistry.SchemaRegistryConfig>? ConfigureSchemaRegistry { get; set; }

        // Layers 2→3: typed Security settings, then raw key/value overrides. Callbacks (layer 4)
        // run afterwards in each Build* method, so precedence is defaults < typed < raw < callback.
        private void ApplyCommon(ClientConfig config)
        {
            Security.ApplyTo(config);
            foreach (var kv in RawConfig)
            {
                config.Set(kv.Key, kv.Value);
            }
        }

        internal ConsumerConfig BuildConsumerConfig(string? groupIdOverride = null)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = BootstrapServers,
                GroupId = string.IsNullOrEmpty(groupIdOverride) ? GroupId : groupIdOverride,
                AutoOffsetReset = AutoOffsetReset,
                EnableAutoCommit = false,
            };

            ApplyCommon(config);
            ConfigureConsumer?.Invoke(config);

            if (config.EnableAutoCommit == true)
            {
                throw new Exceptions.KafkaConfigurationException(
                    "EnableAutoCommit is not supported: Irkalla.Kafka commits offsets manually " +
                    "after successful processing (or after DLQ publish) to guarantee at-least-once delivery.");
            }

            return config;
        }

        internal ProducerConfig BuildProducerConfig()
        {
            var config = new ProducerConfig
            {
                BootstrapServers = BootstrapServers,
                EnableIdempotence = EnableIdempotence,
            };

            ApplyCommon(config);
            ConfigureProducer?.Invoke(config);
            return config;
        }

        internal AdminClientConfig BuildAdminClientConfig()
        {
            var config = new AdminClientConfig
            {
                BootstrapServers = BootstrapServers,
            };

            ApplyCommon(config);
            ConfigureAdminClient?.Invoke(config);
            return config;
        }

        internal Confluent.SchemaRegistry.SchemaRegistryConfig BuildSchemaRegistryConfig()
        {
            var config = new Confluent.SchemaRegistry.SchemaRegistryConfig
            {
                Url = SchemaRegistryUrl
            };

            ConfigureSchemaRegistry?.Invoke(config);
            return config;
        }
    }
}
