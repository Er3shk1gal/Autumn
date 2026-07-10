using Autumn.Kafka.Exceptions;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace Autumn.Kafka.Utils;

/// <summary>
/// Manages Kafka topic lifecycle: existence checks, creation, deletion.
/// </summary>
public class KafkaTopicManager(IAdminClient adminClient, ILogger<KafkaTopicManager> logger)
{
    private readonly IAdminClient _adminClient = adminClient;
    private readonly ILogger<KafkaTopicManager> _logger = logger;

    /// <summary>
    /// Checks if a Kafka topic with the specified name exists.
    /// </summary>
    public bool CheckTopicExists(string topicName)
    {
        try
        {
            var metadata = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            return metadata.Topics.Any(t =>
                t.Topic == topicName && t.Error.Code == ErrorCode.NoError);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to check if topic '{TopicName}' exists", topicName);
            throw new KafkaTopicException($"Failed to check topic '{topicName}'", e);
        }
    }

    /// <summary>
    /// Checks if a topic exists and has the expected number of partitions.
    /// </summary>
    public bool CheckTopicSatisfiesRequirements(string topicName, int numPartitions)
    {
        try
        {
            var metadata = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            return metadata.Topics.Any(t =>
                t.Topic == topicName &&
                t.Error.Code == ErrorCode.NoError &&
                t.Partitions.Count == numPartitions);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to verify topic '{TopicName}' requirements", topicName);
            throw new KafkaTopicException($"Failed to check topic '{topicName}'", e);
        }
    }

    /// <summary>
    /// Checks if a topic contains the specified partition.
    /// </summary>
    public bool CheckTopicContainsPartitions(string topicName, int partition)
    {
        try
        {
            var metadata = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            return metadata.Topics.Any(t =>
                t.Topic == topicName &&
                t.Error.Code == ErrorCode.NoError &&
                t.Partitions.Any(p => p.PartitionId == partition));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to check partitions for topic '{TopicName}'", topicName);
            throw new KafkaTopicException($"Failed to check topic '{topicName}'", e);
        }
    }

    /// <summary>
    /// Creates a new Kafka topic. Returns true if created or already exists.
    /// </summary>
    public async Task<bool> CreateTopicAsync(
        string topicName, int numPartitions, short replicationFactor)
    {
        try
        {
            await _adminClient.CreateTopicsAsync(
            [
                new TopicSpecification
                {
                    Name = topicName,
                    NumPartitions = numPartitions,
                    ReplicationFactor = replicationFactor,
                }
            ]);

            _logger.LogInformation(
                "Topic '{TopicName}' created ({Partitions} partitions, RF={RF})",
                topicName, numPartitions, replicationFactor);
            return true;
        }
        catch (CreateTopicsException e)
            when (e.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            _logger.LogDebug("Topic '{TopicName}' already exists", topicName);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create topic '{TopicName}'", topicName);
            throw new KafkaTopicException($"Failed to create topic '{topicName}'", e);
        }
    }

    /// <summary>
    /// Deletes a Kafka topic.
    /// </summary>
    public async Task<bool> DeleteTopicAsync(string topicName)
    {
        try
        {
            await _adminClient.DeleteTopicsAsync([topicName]);
            _logger.LogInformation("Topic '{TopicName}' deleted", topicName);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete topic '{TopicName}'", topicName);
            throw new KafkaTopicException($"Failed to delete topic '{topicName}'", e);
        }
    }
}