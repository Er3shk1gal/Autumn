using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ReflectionExceptions
{
    public class ReflectionException : Exception
    {
        public ReflectionException()
        {}

        public ReflectionException(string message)
            : base(message)
        {}

        public ReflectionException(string message, Exception innerException)
            : base(message, innerException)
        {}
    }
}