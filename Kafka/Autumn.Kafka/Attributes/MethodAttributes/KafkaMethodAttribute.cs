using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Attributes.MethodAttributes
{
    /// <summary>
    /// Attribute for kafka method,
    /// has to be used with classes annotated with 'KafkaServiceAttribute'
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class KafkaMethodAttribute : Attribute
    {
        public string _methodName { get; set; } = null!;
        public int _partition { get; set; }
        public bool _requiresResponse { get; set; }
        public KafkaMethodAttribute(string methodName, int partition, bool requiresResponse)
        {
            _methodName = methodName;
            _partition = partition;
            _requiresResponse = requiresResponse;
        }
    }
}