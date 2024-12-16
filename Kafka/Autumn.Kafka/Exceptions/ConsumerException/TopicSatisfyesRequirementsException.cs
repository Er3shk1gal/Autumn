using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ProducerExceptions
{
    public class TopicSatisfiesRequirementsException : ConsumerException
    {
        public TopicSatisfiesRequirementsException()
        {
        }

        public TopicSatisfiesRequirementsException(string message)
            : base(message)
        {
        }

        public TopicSatisfiesRequirementsException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}