using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ReflectionExceptions
{
    public class GetSingletonServiceException : ReflectionException
    {
        public GetSingletonServiceException(string message) : base(message)
        {
        }
    }
}