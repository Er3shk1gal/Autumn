using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autumn.Kafka.Exceptions;
using Autumn.Kafka.Exceptions.ConsumerExceptions;
using Autumn.Kafka.Exceptions.ProducerExceptions;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace Autumn.Kafka.Utils
{
    public class KafkaProducer
    {
        private IProducer<string, string> _producer;
        private readonly ILogger<KafkaProducer> _logger;
        private readonly KafkaTopicManager _kafkaTopicManager;
        public KafkaProducer(IProducer<string,string> producer, ILogger<KafkaProducer> logger, KafkaTopicManager kafkaTopicManager)
        {
            _producer = producer;
            _logger = logger;
            _kafkaTopicManager = kafkaTopicManager;
        }
        public async Task<bool> ProduceAsync(TopicConfig topic,int partition, Message<string, string> message)
        {
            try
            {
                bool IsTopicExists = IsTopicAvailable(topic.TopicName, partition);
                if (IsTopicExists)
                {
                    var deliveryResult = await _producer.ProduceAsync(
                            new TopicPartition(topic.TopicName, new Partition(partition)),
                            message);
                    if (deliveryResult.Status == PersistenceStatus.Persisted)
                    {
                        _logger.LogInformation("Message delivery status: Persisted {Result}", deliveryResult.Value);
                        return true;
                    }
                    _logger.LogError("Message delivery status: Not persisted {Result}", deliveryResult.Value);
                    throw new MessageProduceException("Message delivery status: Not persisted" + deliveryResult.Value);
                }
                bool IsTopicCreated = _kafkaTopicManager.CreateTopic(topic.TopicName, topic.PartitionsCount, topic.ReplicationFactor);
                if (IsTopicCreated)
                {
                    var deliveryResult = await _producer.ProduceAsync(new TopicPartition(topic.TopicName, new Partition(partition)), message);
                    if (deliveryResult.Status == PersistenceStatus.Persisted)
                    {
                        _logger.LogInformation("Message delivery status: Persisted {Result}", deliveryResult.Value);
                        return true;
                    }
                    _logger.LogError("Message delivery status: Not persisted {Result}", deliveryResult.Value);
                    throw new MessageProduceException("Message delivery status: Not persisted");
                }
                _logger.LogError("Topic unavailable");
                throw new MessageProduceException("Topic unavailable");
            }
            catch (Exception e)
            {
                if (e is Exceptions.KafkaException)
                {
                    _logger.LogError(e, "Error producing message");
                    throw new ProducerException("Error producing message",e);
                }
                throw;
            }
        }
        private bool IsTopicAvailable(string topicName, int partition)
        {
            try
            {
                bool IsTopicExists = _kafkaTopicManager.CheckTopicContainsPartitions(topicName, partition);
                if (IsTopicExists)
                {
                    return IsTopicExists;
                }
                _logger.LogError("Unable to subscribe to topic");
                throw new ProducerException("Topic unavailable");
            }
            catch (Exception e)
            {
                if (e is Exceptions.KafkaException)
                {
                    _logger.LogError(e,"Error checking topic");
                    throw new ConsumerException("Error checking topic",e);
                }
                _logger.LogError(e,"Unhandled error");
                throw;
            }
        }
    }
}