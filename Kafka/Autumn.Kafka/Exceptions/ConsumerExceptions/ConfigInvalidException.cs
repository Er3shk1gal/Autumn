using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ConsumerExceptions
{
    public class ConfigInvalidException : ConsumerException
    {
        public ConfigInvalidException()
        {
        }

        public ConfigInvalidException(string message)
            : base(message)
        {
        }

        public ConfigInvalidException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}