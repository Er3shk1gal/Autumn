using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Autumn.Kafka.Utils.Models
{
    public class TopicInfo
    {
        public string TopicName { get; set; } = null!;
        public int PartitionNumber { get; set; }
    }
}