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
        public TopicConfig _requestTopic { get; set; }
        public TopicConfig? _responseTopic { get; set; }
        public MessageHandlerType _messageHandlerType { get; set; }
        public KafkaSimpleServiceAttribute(TopicConfig requestTopic, MessageHandlerType messageHandlerType, TopicConfig? responseTopic = null)
        {
            _requestTopic = requestTopic;
            _responseTopic = responseTopic;
            _messageHandlerType = messageHandlerType;
        }
    }
}