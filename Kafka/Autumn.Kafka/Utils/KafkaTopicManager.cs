using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, bool> _verifiedTopics = new();

    /// <summary>
    /// Checks if a Kafka topic with the specified name exists.
    /// </summary>
    public bool CheckTopicExists(string topicName)
    {
        if (_verifiedTopics.ContainsKey(topicName)) return true;

        try
        {
            var metadata = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            var exists = metadata.Topics.Any(t =>
                t.Topic == topicName && t.Error.Code == ErrorCode.NoError);
            
            if (exists) _verifiedTopics.TryAdd(topicName, true);
            return exists;
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
        var cacheKey = $"{topicName}_req_{numPartitions}";
        if (_verifiedTopics.ContainsKey(cacheKey)) return true;

        try
        {
            var metadata = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            var satisfies = metadata.Topics.Any(t =>
                t.Topic == topicName &&
                t.Error.Code == ErrorCode.NoError &&
                t.Partitions.Count == numPartitions);
            
            if (satisfies) _verifiedTopics.TryAdd(cacheKey, true);
            return satisfies;
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
        var cacheKey = $"{topicName}_part_{partition}";
        if (_verifiedTopics.ContainsKey(cacheKey)) return true;

        try
        {
            var metadata = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            var contains = metadata.Topics.Any(t =>
                t.Topic == topicName &&
                t.Error.Code == ErrorCode.NoError &&
                t.Partitions.Any(p => p.PartitionId == partition));
                
            if (contains) _verifiedTopics.TryAdd(cacheKey, true);
            return contains;
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
            _verifiedTopics.TryAdd(topicName, true);
            return true;
        }
        catch (CreateTopicsException e)
            when (e.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            _logger.LogDebug("Topic '{TopicName}' already exists", topicName);
            WarnIfFewerPartitions(topicName, numPartitions);
            _verifiedTopics.TryAdd(topicName, true);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create topic '{TopicName}'", topicName);
            throw new KafkaTopicException($"Failed to create topic '{topicName}'", e);
        }
    }

    // A pre-existing topic keeps its original partition count — CreateTopics does not change it.
    // If it has fewer partitions than requested, ConsumerMode.Auto can't scale beyond that, so warn.
    private void WarnIfFewerPartitions(string topicName, int requestedPartitions)
    {
        if (requestedPartitions <= 1) return;
        try
        {
            var metadata = _adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(10));
            var actual = metadata.Topics.FirstOrDefault(t => t.Topic == topicName)?.Partitions.Count ?? 0;
            if (actual > 0 && actual < requestedPartitions)
            {
                _logger.LogWarning(
                    "Topic '{TopicName}' already exists with {Actual} partition(s), but {Requested} were requested. " +
                    "ConsumerMode.Auto will run at most {Actual} effective consumer(s) for this topic.",
                    topicName, actual, requestedPartitions, actual);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not verify partition count for topic '{TopicName}'", topicName);
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