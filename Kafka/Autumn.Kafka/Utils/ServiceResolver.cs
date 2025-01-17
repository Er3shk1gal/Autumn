using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Autumn.Kafka.Attributes.MethodAttributes;
using Autumn.Kafka.Attributes.ServiceAttributes;
using Autumn.Kafka.Exceptions.ReflectionExceptions;
using Autumn.Kafka.Utils.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Autumn.Kafka.Utils
{
    public static class ServiceResolver
    {
        public static object InvokeMethodByHeader(IServiceProvider serviceProvider,MethodInfo methodInfo,Type service,object? parameter)
        {
            ServiceLifetime lifetime = GetServiceLifetime(service,serviceProvider);
            if(parameter!=null)
            {
                if(lifetime==ServiceLifetime.Scoped)
                {
                    return InvokeMethodWithParameters( methodInfo, GetScopedService(serviceProvider,service), parameter);
                }
                else if(lifetime==ServiceLifetime.Singleton)
                {
                    return InvokeMethodWithParameters( methodInfo, GetSingletonService(serviceProvider,service), parameter);
                }
                else if(lifetime==ServiceLifetime.Transient)
                {
                    return InvokeMethodWithParameters( methodInfo, GetTransientService(serviceProvider,service), parameter);
                }
            }
            if(lifetime==ServiceLifetime.Scoped)
            {
                return InvokeMethodWithoutParameters( methodInfo, GetScopedService(serviceProvider,service));
            }
            else if(lifetime==ServiceLifetime.Singleton)
            {
                return InvokeMethodWithoutParameters( methodInfo, GetSingletonService(serviceProvider,service));
            }
            else if(lifetime==ServiceLifetime.Transient)
            {
                return InvokeMethodWithoutParameters( methodInfo, GetTransientService(serviceProvider,service));
            }
            throw new InvokeMethodException("Failed to invoke method");
        }
        private static object InvokeMethodWithoutParameters(MethodInfo method,object serviceInstance)
        {
            if (method.GetParameters().Length != 0)
            {
                throw new InvokeMethodException("Wrong method implementation: method should not have parameters.");
            }

            if (method.ReturnType == typeof(void))
            {
                method.Invoke(serviceInstance, null);
                return true;
            }
            else
            {
                var result = method.Invoke(serviceInstance, null);
                if (result != null)
                {
                    return result;
                }
            }
            throw new InvokeMethodException("Method invocation failed");
        }

        private static object InvokeMethodWithParameters(MethodInfo method, object serviceInstance, object parameter)
        {

            if (method.GetParameters().Length == 0)
            {
                throw new InvokeMethodException("Wrong method implementation: method should have parameters.");
            }

            if (method.ReturnType == typeof(void))
            {
                method.Invoke(serviceInstance, new [] { parameter });
                return true;
            }
            else
            {
                var result = method.Invoke(serviceInstance, new [] { parameter });
                if (result != null)
                {
                    return result;
                }
            }
            throw new InvokeMethodException("Method invocation failed");
        }
        private static ServiceLifetime GetServiceLifetime(Type service, IServiceProvider serviceProvider)
        {
            var serviceCollection = (serviceProvider as IServiceProvider)?.GetService(typeof(IServiceCollection)) as IServiceCollection;
            if (serviceCollection != null)
            {
                var serviceType = serviceCollection.FirstOrDefault(x=>x.ServiceType==service);
                if(serviceType != null)
                {
                    return serviceType.Lifetime;
                }
            }
            throw new GetServiceLifetimeException("Failed to get service lifetime");
        }
        private static object GetScopedService(IServiceProvider serviceProvider, Type serviceType)
        {
            using (var scope = serviceProvider.CreateScope())
            {
                var serviceInstance = scope.ServiceProvider.GetRequiredService(serviceType.GetInterfaces().FirstOrDefault() ?? throw new GetScopedServiceException("Failed to get scoped service"));
                if (serviceInstance != null)
                {
                    return serviceInstance;
                }
            }
            throw new GetScopedServiceException("Failed to get scoped service");
        }
        private static object GetSingletonService(IServiceProvider serviceProvider, Type serviceType)
        {
            var serviceInstance = serviceProvider.GetRequiredService(serviceType.GetInterfaces().FirstOrDefault() ?? throw new GetSingletonServiceException("Failed to get singleton service"));
            if (serviceInstance != null)
            {
                return serviceInstance;
            }
            throw new GetSingletonServiceException("Failed to get singleton service");
        }
        private static object GetTransientService(IServiceProvider serviceProvider, Type serviceType)
        {
            var serviceInstance = serviceProvider.GetRequiredService(serviceType.GetInterfaces().FirstOrDefault() ?? throw new GetTransientServiceException("Failed to get transient service"));
            if (serviceInstance != null)
            {
                return serviceInstance;
            }
            throw new GetTransientServiceException("Failed to get transient service");
        }
        public static IEnumerable<IGrouping<TopicConfig, Type>> GetKafkaServices()
        {
            return Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.GetCustomAttribute(typeof(KafkaServiceAttribute), false)!=null)
                .GroupBy(t =>
                    ((KafkaServiceAttribute)t.GetCustomAttribute(typeof(KafkaServiceAttribute), false)!).RequestTopicConfig);

        }

        public static IEnumerable<IGrouping<TopicConfig, Type>> GetSimpleKafkaServices()
        {
            return Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.GetCustomAttribute(typeof(KafkaSimpleServiceAttribute), false)!=null)
                .GroupBy(t =>
                    ((KafkaSimpleServiceAttribute)t.GetCustomAttribute(typeof(KafkaSimpleServiceAttribute), false)!).RequestTopic);
        }
        private static IEnumerable<MethodInfo> GetMethodsByAttribute(Type attributeType, Type serviceType)
        {
            var methods = serviceType.GetMethods()
            .Where(m => m.GetCustomAttributes(attributeType, false).Any());
            return new HashSet<MethodInfo>(methods);
        }

        private static IEnumerable<MessageHandlerConfig> X()
        {
            IEnumerable<IGrouping<TopicConfig, Type>> kafkaServices = ServiceResolver.GetKafkaServices();
            IEnumerable<IGrouping<TopicConfig, Type>> simpleKafkaService = ServiceResolver.GetSimpleKafkaServices();

           
            var combinedGroups = kafkaServices
                .Select(g => new { TopicConfig = g.Key, Types = g.ToList() })
                .Concat(simpleKafkaService.Select(g => new { TopicConfig = g.Key, Types = g.ToList() }));

            var finalGrouped = combinedGroups
                .GroupBy(g => g.TopicConfig)
                .Select(g => new
                {
                    TopicConfig = g.Key,
                    Types = g.SelectMany(x => x.Types).ToList()
                });
            var result = finalGrouped
                .Select(group => new
                {
                    TopicConfig = group.TopicConfig, 
                    MethodInfoDictionary = group.Types
                        .Distinct() 
                        .ToDictionary(
                            type => type,
                            type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                                .Where(method => method.GetCustomAttributes(typeof(KafkaMethodAttribute), false).Any() ||
                                                 method.GetCustomAttributes(typeof(KafkaSimpleMethodAttribute), false).Any())
                                .ToList()
                        )
                });
            IEnumerable<MessageHandlerConfig> configs = new List<MessageHandlerConfig>();
            foreach (var topic in result)
            {
                MessageHandlerConfig config = new MessageHandlerConfig();
                
                foreach (var service in topic.MethodInfoDictionary)
                {
                    List<KafkaMethodExecutionConfig> kafkaMethodExecutionConfigs =
                        new List<KafkaMethodExecutionConfig>();
                    foreach (var method in service.Value)
                    {
                        string kafkaServiceName = service.Key.
                        kafkaMethodExecutionConfigs.Add( new KafkaMethodExecutionConfig
                        {
                            KafkaMethodName = null,
                            ServiceMethodPair = null,
                            RequireResponse = false,
                            responseTopicConfig = null,
                            responseTopicPartition = null,
                            KafkaServiceName = 
                        });
                    }
                }
            }
        }
    }
}