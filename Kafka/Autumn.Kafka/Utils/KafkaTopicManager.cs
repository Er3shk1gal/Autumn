using Autumn.Kafka.Exceptions.TopicExceptions;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace Autumn.Kafka.Utils;

public class KafkaTopicManager(IAdminClient adminClient, ILogger<KafkaTopicManager> logger)
{
    private readonly IAdminClient _adminClient = adminClient;
    private readonly ILogger<KafkaTopicManager> _logger = logger;
    /// <summary>
    /// Checks if a Kafka topic with the specified name exists.
    /// </summary>
    /// <param name="topicName">The name of the topic to check.</param>
    /// <returns>True if the topic exists, false otherwise.</returns>
    /// <exception cref="CheckTopicException">Thrown if the topic check fails.</exception>
    public bool CheckTopicExists(string topicName)
    {
        try
        {
            //TODO: debug and check how it works
            var topicExists = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            if (topicExists.Topics.Count == 0)
            {
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred: {e.Message}"); 
            throw new CheckTopicException("Failed to check topic");
        }
    }
    public bool CheckTopicSatisfiesRequirements(string topicName, int numPartitions)
    {
        try
        {
            //TODO: debug and check how it works
            var topicExists = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            if (topicExists.Topics.Count != 0 && topicExists.Topics.Any(t => t.Topic==topicName && t.Partitions.Count==numPartitions))
            {
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred: {e.Message}");
            throw new CheckTopicException("Failed to check topic");
        }
    }
    public bool CheckTopicContainsPartitions(string topicName, int partition)
    {
        try
        {
            var topicExists = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            if (topicExists.Topics.Count != 0 && topicExists.Topics.Any(t => t.Topic==topicName && t.Partitions.Any(t=>t.PartitionId==partition)))
            {
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            _logger.LogError($"An error occurred: {e.Message}");
            throw new CheckTopicException("Failed to check topic");
        }
    }
    /// <summary>
    /// Creates a new Kafka topic with the specified name, number of partitions, and replication factor.
    /// </summary>
    /// <param name="topicName">The name of the topic to create.</param>
    /// <param name="numPartitions">The number of partitions for the topic.</param>
    /// <param name="replicationFactor">The replication factor for the topic.</param>
    /// <returns>True if the topic was successfully created, false otherwise.</returns>
    /// <exception cref="CreateTopicException">Thrown if the topic creation fails.</exception>
    public bool CreateTopic(string topicName, int numPartitions, short replicationFactor)
    {
        try
        {
            var result = _adminClient.CreateTopicsAsync(new TopicSpecification[]
            { 
                new() {
                    Name = topicName,
                    NumPartitions = numPartitions,
                    ReplicationFactor =  replicationFactor,
                    }
            });
            if (result.IsCompleted)
            {
                return true;
            }
            throw new CreateTopicException("Failed to create topic");
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            throw new CreateTopicException("Failed to create topic");
        }
    }
    public bool DeleteTopic(string topicName)
    {
        try
        {
            var result = _adminClient.DeleteTopicsAsync(new List<string> { topicName });
            if (result.IsCompleted)
            {
                return true;
            }
            throw new DeleteTopicException("Failed to delete topic");
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            throw new DeleteTopicException("Failed to delete topic");
        }
    }
}