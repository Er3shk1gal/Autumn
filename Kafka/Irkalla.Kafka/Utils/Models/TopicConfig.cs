namespace Irkalla.Kafka.Utils.Models
{
    /// <summary>
    /// Kafka topic configuration — name, partitions, replication.
    /// </summary>
    public class TopicConfig
    {
        public string TopicName { get; set; } = null!;
        public int PartitionsCount { get; set; } = 1;
        public short ReplicationFactor { get; set; } = 1;
    }
}