using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Utils
{
    public static class ServiceResolver
    {
        public static ServiceMethodPair GetClassAndMethodTypes(string serviceName,string methodName)
        {
            var serviceClasses = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(KafkaServiceNameAttribute), false).Any());
            foreach (var serviceClass in serviceClasses)
            {
                var serviceNameAttr = (KafkaServiceNameAttribute)serviceClass
                    .GetCustomAttributes(typeof(KafkaServiceNameAttribute), false)
                    .FirstOrDefault();
                if (serviceNameAttr != null && serviceNameAttr.ServiceName == serviceName)
                {
                    var methods = serviceClass.GetMethods()
                    .Where(m => m.GetCustomAttributes(typeof(KafkaMethodAttribute), false).Any());
                    foreach (var method in methods)
                    {
                        var methodAttr = (KafkaMethodAttribute)method
                            .GetCustomAttributes(typeof(KafkaMethodAttribute), false)
                            .FirstOrDefault();

                        if (methodAttr != null && methodAttr.MethodName == methodName)
                        {
                            return new ServiceMethodPair()
                            {
                                Service = serviceClass,
                                Method = method,
                            };
                        }
                    }
                }
            }
            throw new UnconfiguredServiceMethodsExeption("Method not found");
        }
    }
}