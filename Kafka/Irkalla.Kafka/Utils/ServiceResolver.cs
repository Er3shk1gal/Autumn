using System.Reflection;
using Irkalla.Kafka.Attributes;
using Irkalla.Kafka.Exceptions;
using Irkalla.Kafka.Utils.Models;

namespace Irkalla.Kafka.Utils
{
    /// <summary>
    /// Resolves service instances from DI and invokes methods via reflection.
    /// </summary>
    public static class ServiceResolver
    {
        /// <summary>
        /// Resolves the service from DI and invokes the specified method.
        /// </summary>
        public static async Task<object?> InvokeMethodAsync(
            IServiceProvider serviceProvider,
            MethodInfo methodInfo,
            Type serviceType,
            IEnumerable<object>? parameters)
        {
            var serviceInstance = ResolveService(serviceProvider, serviceType);
            return await InvokeMethodOnInstanceAsync(methodInfo, serviceInstance, parameters);
        }

        public static readonly object VoidResultMarker = new object();

        private static async Task<object?> InvokeMethodOnInstanceAsync(
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
                var result = method.Invoke(serviceInstance, args);

                if (result is Task task)
                {
                    await task;

                    // `async Task` compiles to Task<VoidTaskResult>, whose runtime type IS generic —
                    // so the value/void decision must use the METHOD's declared return type, not
                    // task.GetType(). Otherwise a void async handler leaks a VoidTaskResult struct.
                    if (method.ReturnType.IsGenericType &&
                        method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        return task.GetType().GetProperty("Result")?.GetValue(task);
                    }

                    return VoidResultMarker;
                }
                
                if (result is ValueTask valueTask)
                {
                    await valueTask;
                    return VoidResultMarker;
                }

                var type = result?.GetType();
                if (type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    var asTaskMethod = type.GetMethod("AsTask");
                    if (asTaskMethod != null)
                    {
                        var taskResult = (Task)asTaskMethod.Invoke(result, null)!;
                        await taskResult;
                        var resultProperty = taskResult.GetType().GetProperty("Result");
                        return resultProperty?.GetValue(taskResult);
                    }
                }

                if (method.ReturnType == typeof(void))
                {
                    return VoidResultMarker;
                }

                return result;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Unwrap reflection wrapper to expose the actual exception
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw ex.InnerException;
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

            // Check implemented interfaces
            var interfaces = serviceType.GetInterfaces();
            if (interfaces.Length > 1)
            {
                throw new KafkaServiceResolutionException(
                    $"Ambiguous resolution for service '{serviceType.Name}'. " +
                    $"It implements multiple interfaces ({string.Join(", ", interfaces.Select(i => i.Name))}). " +
                    "Register the service by its concrete type or specify the service type explicitly.");
            }

            if (interfaces.Length == 1)
            {
                var interfaceType = interfaces[0];
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

    }
}