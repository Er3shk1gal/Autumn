using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autumn.Kafka.Attributes.MethodAttributes;
using Autumn.Kafka.Attributes.ServiceAttributes;
using Autumn.Kafka.Utils;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Autumn.Kafka.MessageHandlers;

//TODO: write factory for Message handlers
public class MessageHandlerFactory
{
    //TODO: Add 
    public static IEnumerable<MessageHandler> CreateHandlers(IServiceProvider serviceProvider)
    {
        var handlers = new List<MessageHandler>();
        var assembly = Assembly.GetExecutingAssembly();
        
        var handlerConfigs = CreateKafkaMessageHandlerConfig().ToList();

        var handlerTypes = assembly.GetTypes()
            .Where(t => typeof(MessageHandler).IsAssignableFrom(t) && 
                        !t.IsAbstract);

        foreach (var type in handlerTypes)
        {
            var config = handlerConfigs.FirstOrDefault(c => 
                c.kafkaMethodExecutionConfigs.Any(k => k.ServiceMethodPair.Service == type));
                
            if (config == null)
            {
                config = new MessageHandlerConfig()
                {
                    RequestTopicConfig = new TopicConfig(),
                    kafkaMethodExecutionConfigs = new HashSet<KafkaMethodExecutionConfig>()
                };
            }

            var producer = serviceProvider.GetRequiredService<KafkaProducer>();
            var consumer = serviceProvider.GetRequiredService<IConsumer<string, string>>();
            var loggerType = typeof(ILogger<>).MakeGenericType(type);
            var logger = serviceProvider.GetRequiredService(loggerType);

            var handler = (MessageHandler)ActivatorUtilities.CreateInstance(
                serviceProvider, 
                type, 
                producer, 
                consumer, 
                config, 
                logger, 
                serviceProvider);
                
            handlers.Add(handler);
        }

        return handlers;
    }

    private static IEnumerable<MessageHandlerConfig> CreateKafkaMessageHandlerConfig()
    {
        var configs = new List<MessageHandlerConfig>();
        var assembly = Assembly.GetExecutingAssembly();

        var serviceTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttributes<KafkaServiceAttribute>().Any() || 
                       t.GetCustomAttributes<KafkaSimpleServiceAttribute>().Any());

        foreach (var serviceType in serviceTypes)
        {
            var isSimpleService = serviceType.GetCustomAttributes<KafkaSimpleServiceAttribute>().Any();
            var serviceAttribute = isSimpleService 
                ? (Attribute)serviceType.GetCustomAttributes<KafkaSimpleServiceAttribute>().First() 
                : serviceType.GetCustomAttributes<KafkaServiceAttribute>().First();
         
            var methods = serviceType.GetMethods()
                .Where(m => isSimpleService 
                    ? m.GetCustomAttributes<KafkaSimpleMethodAttribute>().Any()
                    : m.GetCustomAttributes<KafkaMethodAttribute>().Any());

            if (!methods.Any())
            {
                throw new InvalidOperationException($"Service {serviceType.Name} has no valid Kafka methods");
            }

            var methodConfigs = methods.Select(m => 
            {
                var methodAttr = isSimpleService
                    ? (Attribute)m.GetCustomAttributes<KafkaSimpleMethodAttribute>().First()
                    : m.GetCustomAttributes<KafkaMethodAttribute>().First();

               var config = new KafkaMethodExecutionConfig();

                if (isSimpleService)
                {
                    var simpleAttr = (KafkaSimpleMethodAttribute)methodAttr;
                    var serviceAttr = (KafkaSimpleServiceAttribute)serviceAttribute;
                    config.KafkaMethodName = simpleAttr.MethodName;
                    config.responseTopicConfig = serviceAttr.ResponseTopic;
                    config.RequireResponse = simpleAttr.RequiresResponse;
                    config.KafkaServiceName = serviceAttr.KafkaServiceName;
                    config.responseTopicPartition = serviceAttr.ResponsePartition;
                    config.ServiceMethodPair = new ServiceMethodPair()
                    {
                        Service = serviceType,
                        Method = m,
                        Parameters = m.GetParameters()
                    };
                    
                }
                else 
                {
                    var kafkaMethodAttr = (KafkaMethodAttribute)methodAttr;
                    var serviceAttr = (KafkaServiceAttribute)serviceAttribute;
                    config.KafkaMethodName = kafkaMethodAttr.MethodName;
                    config.responseTopicConfig = serviceAttr.ResponseTopicConfig;
                    config.responseTopicPartition = kafkaMethodAttr.Partition;
                    config.RequireResponse = kafkaMethodAttr.RequiresResponse;
                    config.KafkaServiceName = serviceAttr.KafkaServiceName;
                    config.ServiceMethodPair = new ServiceMethodPair()
                    {
                        Service = serviceType,
                        Method = m,
                        Parameters = m.GetParameters()
                    };
                    
                }

                return config;
            }).ToList();
        }

        return configs;
    } 
}
