using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Newtonsoft.Json.Linq;

namespace Autumn.Kafka.MessageHandlers
{
    public class JsonMessageHandler
    {
        private readonly IProducer<string,string> _producer;
        private readonly IConsumer<string,string> _consumer;
        private readonly TopicConfig _requestTopicConfig;
        public JsonMessageHandler(IProducer<string, string> producer, IConsumer<string, string> consumer, TopicConfig requestTopicConfig)
        {
            _producer = producer;
            _consumer = consumer;
            _requestTopicConfig = requestTopicConfig;
        }
        public async Task HandleMessage()
        {

        }
    }
}