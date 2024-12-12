using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.Kafka;
using Newtonsoft.Json.Linq;

namespace Autumn.Kafka.MessageHandlers
{
    public abstract class JsonMessageHandler
    {
        private readonly IProducer<string,JObject> _producer;
        private readonly IConsumer<string,JObject> _consumer;
    }
}