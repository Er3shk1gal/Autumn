using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autumn.Kafka.Utils.Models;

namespace Autumn.Kafka.Attributes.ServiceAttributes
{
    //TODO: Validators inside MessageHandlers factory, to verify topics, partitions, methods and etc.
    /// <summary>
    /// Attribute for advanced kafka service with request and response topics with many partitions,
    /// methods has to be annotated with 'KafkaMethodAttribute' and assigned to partitions,
    /// if responding to requests is not required leave responseTopic null
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class KafkaServiceAttribute : Attribute
    {
        public TopicConfig RequestTopicConfig { get; set; }
        public TopicConfig? ResponseTopicConfig { get; set;}
        public string KafkaServiceName { get; set; }
        public MessageHandlerType MessageHandlerType { get; set; }
        public KafkaServiceAttribute(TopicConfig requestTopicConfig, MessageHandlerType messageHandlerType, string kafkaServiceName, TopicConfig? responseTopicConfig = null)
        {
            RequestTopicConfig = requestTopicConfig;
            ResponseTopicConfig = responseTopicConfig;
            MessageHandlerType = messageHandlerType;
            KafkaServiceName = kafkaServiceName;
        }
    }
}