using System.Reflection;
using Autumn.Kafka.Attributes;
using Autumn.Kafka.Configuration;
using Autumn.Kafka.Exceptions;
using Autumn.Kafka.Utils;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Autumn.Kafka.MessageHandlers;

/// <summary>
/// Scans assemblies for <see cref="KafkaServiceAttribute"/> classes and creates
/// corresponding <see cref="MessageHandler"/> instances.
/// </summary>
public static class MessageHandlerFactory
{
    /// <summary>
    /// Creates message handlers for all discovered Kafka services.
    /// </summary>
    /// <param name="serviceProvider">The application's service provider.</param>
    /// <param name="targetAssembly">The assembly to scan. If null, scans the entry assembly.</param>
    public static IEnumerable<MessageHandler> CreateHandlers(
        IServiceProvider serviceProvider,
        Assembly? targetAssembly = null)
    {
        var assembly = targetAssembly
            ?? Assembly.GetEntryAssembly()
            ?? throw new KafkaConfigurationException(
                "Unable to determine entry assembly. Provide the target assembly via AutumnKafkaOptions.ServiceAssembly.");

        var handlerConfigs = BuildHandlerConfigs(assembly).ToList();

        if (handlerConfigs.Count == 0)
        {
            return [];
        }

        var handlers = new List<MessageHandler>();

        foreach (var config in handlerConfigs)
        {
            var handler = CreateHandler(config, serviceProvider);
            handlers.Add(handler);
        }

        return handlers;
    }

    private static MessageHandler CreateHandler(
        MessageHandlerConfig config,
        IServiceProvider serviceProvider)
    {
        var producer = serviceProvider.GetRequiredService<KafkaProducer>();
        var consumer = serviceProvider.GetRequiredService<IConsumer<string, string>>();
        var logger = serviceProvider.GetRequiredService<ILogger<JsonMessageHandler>>();

        return new JsonMessageHandler(producer, consumer, config, logger, serviceProvider);
    }

    /// <summary>
    /// Scans the assembly and builds one <see cref="MessageHandlerConfig"/> per unique request topic.
    /// </summary>
    public static IEnumerable<MessageHandlerConfig> BuildHandlerConfigs(Assembly assembly)
    {
        var serviceTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<KafkaServiceAttribute>() != null)
            .ToList();

        if (serviceTypes.Count == 0)
        {
            return [];
        }

        // Group services by request topic — services sharing a topic share a consumer
        var configsByTopic = new Dictionary<string, MessageHandlerConfig>();

        foreach (var serviceType in serviceTypes)
        {
            var serviceAttr = serviceType.GetCustomAttribute<KafkaServiceAttribute>()!;

            var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<KafkaMethodAttribute>() != null)
                .ToList();

            if (methods.Count == 0)
            {
                throw new KafkaConfigurationException(
                    $"Service '{serviceType.Name}' is decorated with [KafkaService] " +
                    "but has no methods decorated with [KafkaMethod].");
            }

            // Get or create the handler config for this topic
            if (!configsByTopic.TryGetValue(serviceAttr.RequestTopic, out var handlerConfig))
            {
                handlerConfig = new MessageHandlerConfig
                {
                    RequestTopicConfig = new TopicConfig
                    {
                        TopicName = serviceAttr.RequestTopic,
                        PartitionsCount = serviceAttr.RequestPartitions,
                        ReplicationFactor = serviceAttr.RequestReplicationFactor,
                    },
                    KafkaMethodExecutionConfigs = new HashSet<KafkaMethodExecutionConfig>(),
                };
                configsByTopic[serviceAttr.RequestTopic] = handlerConfig;
            }

            // Build execution config for each method
            foreach (var method in methods)
            {
                var methodAttr = method.GetCustomAttribute<KafkaMethodAttribute>()!;

                var responsePartition = methodAttr.ResponsePartition >= 0
                    ? methodAttr.ResponsePartition
                    : serviceAttr.DefaultResponsePartition;

                TopicConfig? responseTopicConfig = null;
                if (serviceAttr.ResponseTopic != null)
                {
                    responseTopicConfig = new TopicConfig
                    {
                        TopicName = serviceAttr.ResponseTopic,
                        PartitionsCount = serviceAttr.ResponsePartitions,
                        ReplicationFactor = serviceAttr.ResponseReplicationFactor,
                    };
                }

                var executionConfig = new KafkaMethodExecutionConfig
                {
                    KafkaMethodName = methodAttr.MethodName,
                    RequireResponse = methodAttr.RequiresResponse,
                    KafkaServiceName = serviceAttr.ServiceName,
                    ResponseTopicConfig = responseTopicConfig,
                    ResponseTopicPartition = responsePartition,
                    ServiceMethodPair = new ServiceMethodPair
                    {
                        Service = serviceType,
                        Method = method,
                        Parameters = method.GetParameters(),
                    },
                };

                handlerConfig.KafkaMethodExecutionConfigs.Add(executionConfig);
            }
        }

        return configsByTopic.Values;
    }
}
