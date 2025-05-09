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
        #region Fields

        private readonly string _methodName;
        private readonly int _partition;
        private readonly bool _requiresResponse;

        #endregion


        #region Properties

        /// <summary>
        /// Name of the kafka method
        /// </summary>
        public string MethodName => _methodName;

        /// <summary>
        /// Partition for the kafka consumer
        /// </summary>
        public int Partition => _partition;

        /// <summary>
        /// Requires a response
        /// </summary>
        public bool RequiresResponse => _requiresResponse;
        
        #endregion
        public KafkaMethodAttribute(string methodName, int partition, bool requiresResponse)
        {
            _methodName = methodName;
            _partition = partition;
            _requiresResponse = requiresResponse;
        }
    }
}