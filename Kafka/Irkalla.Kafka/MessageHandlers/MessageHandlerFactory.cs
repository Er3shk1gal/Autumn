using System.Reflection;
using Irkalla.Kafka.Attributes;
using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Exceptions;
using Irkalla.Kafka.Utils;
using Irkalla.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Irkalla.Kafka.MessageHandlers;

/// <summary>
/// Scans assemblies for <see cref="KafkaServiceAttribute"/> classes and creates
/// corresponding <see cref="MessageHandlerConfig"/> instances.
/// </summary>
public static class MessageHandlerFactory
{
    /// <summary>
    /// Scans the assembly and builds one <see cref="MessageHandlerConfig"/> per unique request topic.
    /// Performs strict configuration validation.
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
                    HandlerType = serviceAttr.HandlerType,
                    KafkaMethodExecutionConfigs = new Dictionary<string, KafkaMethodExecutionConfig>(),
                };
                configsByTopic[serviceAttr.RequestTopic] = handlerConfig;
            }
            else
            {
                // Validate HandlerType matches across services sharing the same topic
                if (handlerConfig.HandlerType != serviceAttr.HandlerType)
                {
                    throw new KafkaConfigurationException(
                        $"Topic '{serviceAttr.RequestTopic}' is configured with conflicting HandlerTypes: " +
                        $"'{handlerConfig.HandlerType}' and '{serviceAttr.HandlerType}'. " +
                        "All services sharing a RequestTopic must use the same HandlerType.");
                }
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

                // Validate RequiresResponse has a ResponseTopic
                if (methodAttr.RequiresResponse && responseTopicConfig == null)
                {
                    throw new KafkaConfigurationException(
                        $"Method '{methodAttr.MethodName}' in service '{serviceType.Name}' requires a response, " +
                        "but no ResponseTopic is configured on the [KafkaService] attribute.");
                }

                ValidateMethodParameters(method, serviceAttr.HandlerType, methodAttr.MethodName);

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

                // Validate no duplicate MethodName on the same topic
                if (!handlerConfig.KafkaMethodExecutionConfigs.TryAdd(methodAttr.MethodName, executionConfig))
                {
                    throw new KafkaConfigurationException(
                        $"Duplicate KafkaMethodName '{methodAttr.MethodName}' found for topic '{serviceAttr.RequestTopic}'. " +
                        "Each method on a topic must have a unique MethodName.");
                }
            }
        }

        return configsByTopic.Values;
    }

    private static void ValidateMethodParameters(MethodInfo method, Attributes.MessageHandlerType handlerType, string kafkaMethodName)
    {
        var parameters = method.GetParameters();
        int payloadCount = 0;

        foreach (var param in parameters)
        {
            if (param.ParameterType == typeof(CancellationToken))
            {
                continue;
            }

            payloadCount++;
            
            if (handlerType == Attributes.MessageHandlerType.PROTOBUF)
            {
                // Check if it implements Google.Protobuf.IMessage<T>
                var interfaces = param.ParameterType.GetInterfaces();
                bool isProtobufMessage = interfaces.Any(i => 
                    i.IsGenericType && 
                    i.GetGenericTypeDefinition().FullName == "Google.Protobuf.IMessage`1");

                if (!isProtobufMessage)
                {
                    throw new KafkaConfigurationException(
                        $"Method '{kafkaMethodName}' is configured for PROTOBUF, but parameter '{param.Name}' of type '{param.ParameterType.Name}' " +
                        "does not implement Google.Protobuf.IMessage<T>.");
                }
            }
        }

        if (payloadCount > 1)
        {
            throw new KafkaConfigurationException(
                $"Method '{kafkaMethodName}' has {payloadCount} payload parameters. " +
                "A KafkaMethod can accept at most one payload parameter (plus an optional CancellationToken).");
        }
    }
}
