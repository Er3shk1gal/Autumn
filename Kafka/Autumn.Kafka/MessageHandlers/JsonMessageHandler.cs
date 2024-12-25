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
        private readonly MessageHandlerConfig _messageHandlerConfig;
        public JsonMessageHandler(IProducer<string, string> producer, IConsumer<string, string> consumer, TopicConfig requestTopicConfig, MessageHandlerConfig messageHandlerConfig)
        {
            _producer = producer;
            _consumer = consumer;
            _requestTopicConfig = requestTopicConfig;
            _messageHandlerConfig = messageHandlerConfig;
        }
        public async Task Consume()
        {
            try
            {
                _consumer.Assign(GetPartitions());
                while (true)
                {
                    var message = _consumer.Consume();
                    var messageObject = JObject.Parse(message.Value);
                    Console.WriteLine($"Message received: {messageObject}");
            
            }
        }
        private List<TopicPartition> GetPartitions()
        {
            var partitions = new List<TopicPartition>();
            for (int partition = 0; partition < _requestTopicConfig.PartitionsCount; partition++)
            {
                partitions.Add(new TopicPartition(_requestTopicConfig.TopicName, partition));
            }
            return partitions;
        }
        public async Task HandleMessage()
        {

        }
    }
}