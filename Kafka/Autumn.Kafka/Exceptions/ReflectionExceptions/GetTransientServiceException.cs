using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ReflectionExceptions
{
    public class GetTransientServiceException : ReflectionException
    {
        public GetTransientServiceException(string message) : base(message)
        {
        }
    }
}