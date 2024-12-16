using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autumn.Kafka.Utils.Models;

namespace Autumn.Kafka.Attributes
{
    /// <summary>
    /// Attribute for simple service with request and response topics with one partition,
    /// if responding to requests is not required leave responseTopic null
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class KafkaSimpleServiceAttribute : Attribute
    {
        public TopicInfo requestTopic { get; set; }
        public TopicInfo? responseTopic { get; set; }
    }
}