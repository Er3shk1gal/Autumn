using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Autumn.Kafka.Utils.Models;

namespace Autumn.Kafka.Attributes.ServiceAttributes
{
    //TODO: Validators inside MessageHandlers factory, to verify topics, partitions, methods and etc.
    /// <summary>
    /// Attribute for simple service with request and response topics with one partition,
    /// methods has to be annotated with 'KafkaMethodAttribute',
    /// if responding to requests is not required leave responseTopic null
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class KafkaSimpleServiceAttribute : Attribute
    {
        public TopicConfig RequestTopic { get; set; }
        public TopicConfig? ResponseTopic { get; set; }
        public string KafkaServiceMethod { get; set; }
        public MessageHandlerType MessageHandlerType { get; set; }
        public KafkaSimpleServiceAttribute(TopicConfig requestTopic, MessageHandlerType messageHandlerType, string kafkaServiceMethod, TopicConfig? responseTopic = null)
        {
            RequestTopic = requestTopic;
            ResponseTopic = responseTopic;
            MessageHandlerType = messageHandlerType;
            KafkaServiceMethod = kafkaServiceMethod;
        }
    }
}