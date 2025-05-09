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
        #region Fields
        
        private readonly TopicConfig _requestTopicConfig;
        private readonly MessageHandlerType _messageHandlerType;
        private readonly string _kafkaServiceName;
        private readonly TopicConfig? _responseTopicConfig;
            
        #endregion

        #region Properties
        
        public TopicConfig RequestTopicConfig => _requestTopicConfig;
        public TopicConfig? ResponseTopicConfig => _responseTopicConfig;
        public string KafkaServiceName => _kafkaServiceName;
        public MessageHandlerType MessageHandlerType => _messageHandlerType;
        
        #endregion
        
        #region Constructor
        
        public KafkaServiceAttribute(TopicConfig requestTopicConfig, MessageHandlerType messageHandlerType, string kafkaServiceName, int responsePartition, TopicConfig? responseTopicConfig = null)
        {
            _requestTopicConfig = requestTopicConfig;
            _responseTopicConfig = responseTopicConfig;
            _messageHandlerType = messageHandlerType;
            _kafkaServiceName = kafkaServiceName;
        }
        
        #endregion

    
    }
}