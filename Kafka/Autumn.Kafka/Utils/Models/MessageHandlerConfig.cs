namespace Autumn.Kafka.Utils.Models
{
    /// <summary>
    /// Configuration for a single message handler instance — binds a request topic
    /// to a set of method execution configs.
    /// One <see cref="MessageHandlerConfig"/> = one consumer = one topic.
    /// </summary>
    public class MessageHandlerConfig
    {
        public TopicConfig RequestTopicConfig { get; set; } = null!;
        public Attributes.MessageHandlerType HandlerType { get; set; } = Attributes.MessageHandlerType.JSON;
        public Dictionary<string, KafkaMethodExecutionConfig> KafkaMethodExecutionConfigs { get; set; } = [];
    }
}