using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Utils.Models
{
    public class TopicConfig
    {
        public string TopicName { get; set; } = null!;
        public int PartitionsCount {get;set;}
        public short ReplicationFactor {get;set;}
    }
}