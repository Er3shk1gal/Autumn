using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Autumn.Kafka.Utils.Models
{
    public class ServiceMethodPair
    {
        public Type Service { get; set; } = null!;
        public MethodInfo Method { get; set; } = null!;
        public ParameterInfo? Parameter { get; set; }
    }
}