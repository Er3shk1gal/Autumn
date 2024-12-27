using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ConsumerExceptions
{
    public class UnconfiguredServiceMethodsException : ConsumerException
    {
        public UnconfiguredServiceMethodsException()
        {
        }

        public UnconfiguredServiceMethodsException(string message)
            : base(message)
        {
        }

        public UnconfiguredServiceMethodsException(string message, Exception innerException)
            : base(message, innerException)
        {
    }
    }
}