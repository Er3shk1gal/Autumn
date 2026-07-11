namespace Irkalla.Kafka.Utils.Models
{
    /// <summary>
    /// Runtime configuration for a single Kafka method handler.
    /// Maps a method name to its service type, method info, and response settings.
    /// </summary>
    public class KafkaMethodExecutionConfig
    {
        public string KafkaMethodName { get; set; } = null!;
        public ServiceMethodPair ServiceMethodPair { get; set; } = null!;
        public bool RequireResponse { get; set; }
        public TopicConfig? ResponseTopicConfig { get; set; }
        public int? ResponseTopicPartition { get; set; }
        public string? KafkaServiceName { get; set; }
    }
}