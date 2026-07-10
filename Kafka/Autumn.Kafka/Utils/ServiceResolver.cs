using System.Reflection;
using Autumn.Kafka.Attributes;
using Autumn.Kafka.Exceptions;
using Autumn.Kafka.Utils.Models;

namespace Autumn.Kafka.Utils
{
    /// <summary>
    /// Resolves service instances from DI and invokes methods via reflection.
    /// </summary>
    public static class ServiceResolver
    {
        /// <summary>
        /// Resolves the service from DI and invokes the specified method.
        /// </summary>
        public static object InvokeMethod(
            IServiceProvider serviceProvider,
            MethodInfo methodInfo,
            Type serviceType,
            IEnumerable<object>? parameters)
        {
            var serviceInstance = ResolveService(serviceProvider, serviceType);
            return InvokeMethodOnInstance(methodInfo, serviceInstance, parameters);
        }

        private static object InvokeMethodOnInstance(
            MethodInfo method,
            object serviceInstance,
            IEnumerable<object>? parameters)
        {
            var methodParams = method.GetParameters();
            var hasParameters = parameters != null && parameters.Any();

            if (hasParameters && methodParams.Length == 0)
            {
                throw new KafkaServiceResolutionException(
                    $"Method '{method.Name}' does not accept parameters, but parameters were provided.");
            }

            if (!hasParameters && methodParams.Length > 0)
            {
                throw new KafkaServiceResolutionException(
                    $"Method '{method.Name}' requires {methodParams.Length} parameter(s), but none were provided.");
            }

            try
            {
                var args = hasParameters ? parameters!.ToArray() : null;

                if (method.ReturnType == typeof(void))
                {
                    method.Invoke(serviceInstance, args);
                    return true;
                }

                var result = method.Invoke(serviceInstance, args);
                return result ?? true;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Unwrap reflection wrapper to expose the actual exception
                throw new KafkaServiceResolutionException(
                    $"Method '{method.Name}' threw an exception: {ex.InnerException.Message}",
                    ex.InnerException);
            }
        }

        /// <summary>
        /// Resolves a service instance from the DI container.
        /// </summary>
        private static object ResolveService(IServiceProvider serviceProvider, Type serviceType)
        {
            // Try to resolve by the concrete type
            var service = serviceProvider.GetService(serviceType);
            if (service != null)
            {
                return service;
            }

            // Try to resolve by the first implemented interface
            var interfaceType = serviceType.GetInterfaces().FirstOrDefault();
            if (interfaceType != null)
            {
                service = serviceProvider.GetService(interfaceType);
                if (service != null)
                {
                    return service;
                }
            }

            throw new KafkaServiceResolutionException(
                $"Failed to resolve service '{serviceType.Name}' from the DI container. " +
                "Ensure the service is registered in your DI configuration.");
        }

        /// <summary>
        /// Scans the specified assembly for classes decorated with <see cref="KafkaServiceAttribute"/>.
        /// </summary>
        public static IEnumerable<Type> GetKafkaServiceTypes(Assembly assembly)
        {
            return assembly.GetTypes()
                .Where(t => t.GetCustomAttribute<KafkaServiceAttribute>() != null);
        }
    }
}