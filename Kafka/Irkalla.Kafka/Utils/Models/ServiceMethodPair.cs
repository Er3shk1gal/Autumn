using System.Reflection;

namespace Irkalla.Kafka.Utils.Models
{
    /// <summary>
    /// Links a service type to a specific method and its parameter metadata.
    /// Used internally for reflective method invocation.
    /// </summary>
    public class ServiceMethodPair
    {
        public Type Service { get; set; } = null!;
        public MethodInfo Method { get; set; } = null!;
        public IEnumerable<ParameterInfo>? Parameters { get; set; }
    }
}