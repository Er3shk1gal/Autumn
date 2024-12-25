using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Utils.Models
{
    public class MessageHandlerConfig
    {
        HashSet<KafkaMethodExecutionConfig> kafkaMethodExecutionConfigs {get;set;} = null!;
    }
}