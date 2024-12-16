using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Attributes.MethodAttributes
{
    /// <summary>
    /// Attribute for simple kafka method, has to be used with classes annotated with 'KafkaSimpleServiceAttribute'
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class KafkaSimpleMethodAttribute : Attribute
    {
        public string _methodName { get; set; } = null!;
        public bool _requiresResponse { get; set; }
        public KafkaSimpleMethodAttribute(string methodName, bool requiresResponse)
        {
            _methodName = methodName;
            _requiresResponse = requiresResponse;
        }
    }
}