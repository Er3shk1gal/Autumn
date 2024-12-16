using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Exceptions.ProducerExceptions
{
   public class ConfigureMessageBusException : KafkaException
   {
      public ConfigureMessageBusException() {}
      public ConfigureMessageBusException(string message) : base(message) {}
      public ConfigureMessageBusException(string message, System.Exception inner) : base(message, inner) {}
   }
}