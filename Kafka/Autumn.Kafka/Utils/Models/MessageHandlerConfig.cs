using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Utils.Models
{
    public class MessageHandlerConfig
    {
        public HashSet<KafkaMethodExecutionConfig> kafkaMethodExecutionConfigs {get;set;} = null!;
    }
}