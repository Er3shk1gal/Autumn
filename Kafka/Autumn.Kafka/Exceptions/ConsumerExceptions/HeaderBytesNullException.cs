using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ConsumerExceptions
{
    public class HeaderBytesNullException : ConsumerException
    {
        public HeaderBytesNullException()
        {
        }

        public HeaderBytesNullException(string message)
            : base(message)
        {
        }

        public HeaderBytesNullException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}