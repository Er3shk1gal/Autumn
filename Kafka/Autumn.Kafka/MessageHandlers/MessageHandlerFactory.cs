using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Autumn.Kafka.Attributes.MethodAttributes;
using Autumn.Kafka.Attributes.ServiceAttributes;
using Autumn.Kafka.Utils;
using Autumn.Kafka.Utils.Models;

namespace Autumn.Kafka.MessageHandlers
{
    //TODO: write factory for Message handlers
    public class MessageHandlerFactory
    {
        //TODO: Add 
        public static IEnumerable<MessageHandler> CreateHandlers()
        {
            var handlers = new List<MessageHandler>();
            var assembly = Assembly.GetExecutingAssembly();

            var handlerTypes = assembly.GetTypes()
                .Where(t => typeof(MessageHandler).IsAssignableFrom(t) && 
                            !t.IsAbstract);

            foreach (var type in handlerTypes)
            {
                var handler = (MessageHandler)Activator.CreateInstance(type)!;
                handlers.Add(handler);
            }

            return handlers;
        }

        private static IEnumerable<MessageHandlerConfig> CreateKafkaMessageHandlerConfig()
        {
            
           

            


        }
         
        
        
    }
}