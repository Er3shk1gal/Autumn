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

        #region Fields
        
        private readonly string _methodName;
        private readonly bool _requiresResponse;

        #endregion

        #region Properties

        /// <summary>
        /// Name of the method to be called
        /// </summary>
        public string MethodName => _methodName;

        /// <summary>
        /// If true, the handler should return a response
        /// </summary>
        public bool RequiresResponse => _requiresResponse;
        
        #endregion
        
        public KafkaSimpleMethodAttribute(string methodName, bool requiresResponse)
        {
            _methodName = methodName;
            _requiresResponse = requiresResponse;
        }
        
        
    }
}