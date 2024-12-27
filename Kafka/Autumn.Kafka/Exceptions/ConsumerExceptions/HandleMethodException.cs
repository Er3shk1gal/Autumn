using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ConsumerExceptions
{
    public class HandleMethodException : ConsumerException
    {
        public HandleMethodException()
        {
        }

        public HandleMethodException(string message)
            : base(message)
        {
        }

        public HandleMethodException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}