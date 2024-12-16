using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ReflectionExceptions
{
    public class GetScopedServiceException : ReflectionException
    {
        public GetScopedServiceException(string message) : base(message)
        {
        }
    }
}