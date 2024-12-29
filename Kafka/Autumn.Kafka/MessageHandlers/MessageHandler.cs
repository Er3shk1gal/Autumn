using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.MessageHandlers
{
    public abstract class MessageHandler
    {
        public abstract Task Consume();
    }
}