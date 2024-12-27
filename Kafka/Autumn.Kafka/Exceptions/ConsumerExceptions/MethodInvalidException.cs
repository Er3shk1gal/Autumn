using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ConsumerExceptions
{
    public class MethodInvalidException : ConsumerException
    {
        public MethodInvalidException()
        {
        }

        public MethodInvalidException(string message)
            : base(message)
        {
        }

        public MethodInvalidException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}