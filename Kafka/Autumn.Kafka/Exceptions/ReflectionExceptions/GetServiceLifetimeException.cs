using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ReflectionExceptions
{
    public class GetServiceLifetimeException : ReflectionException
    {
        public GetServiceLifetimeException(string message) : base(message)
        {
        }
    }
}