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
        #region Fields
        
        private readonly TopicConfig _requestTopic;
        private readonly TopicConfig? _responseTopic;
        private readonly string _kafkaServiceName;
        private readonly MessageHandlerType _messageHandlerType;
        private readonly int _responsePartition; 
        
        #endregion
        
        
        #region Properties

        public TopicConfig RequestTopic => _requestTopic;
        public TopicConfig? ResponseTopic => _responseTopic;
        public string KafkaServiceName => _kafkaServiceName;
        public MessageHandlerType MessageHandlerType => _messageHandlerType;
        public int ResponsePartition => _responsePartition;
        
        #endregion
        #region Constructor
        
        public KafkaSimpleServiceAttribute(TopicConfig requestTopic, MessageHandlerType messageHandlerType, string kafkaServiceName, int responsePartition, TopicConfig? responseTopic = null)
        {
            _requestTopic = requestTopic;
            _responseTopic = responseTopic;
            _messageHandlerType = messageHandlerType;
            _kafkaServiceName = kafkaServiceName;
            _responsePartition = responsePartition;
        }
        
        #endregion
    }
}