using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autumn.Kafka.Attributes.MethodAttributes;
using Autumn.Kafka.Attributes.ServiceAttributes;

namespace Autumn.Kafka.Attributes.Validators
{
    public static class KafkaServiceAttributeValidator
    {
        public static void ValidateAttributes<T>()
        {
            var type = typeof(T);
            var hasClassAttribute = type.GetCustomAttributes(typeof(KafkaServiceAttribute), false).Any();

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in methods)
            {
                var hasMethodAttribute = method.GetCustomAttributes(typeof(KafkaMethodAttribute), false).Any();
                if (hasMethodAttribute && !hasClassAttribute)
                {
                    throw new InvalidOperationException($"Method '{method.Name}' cannot have 'KafkaMethodAttribute' without the class having 'MyClassAttribute'.");
                }
            }
        }
    }
}