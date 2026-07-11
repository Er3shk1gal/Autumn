namespace Irkalla.Kafka.Attributes
{
    /// <summary>
    /// Message serialization format for Kafka handlers.
    /// </summary>
    public enum MessageHandlerType
    {
        /// <summary>JSON serialization using Newtonsoft.Json.</summary>
        JSON,

        /// <summary>Apache Avro serialization (not yet implemented).</summary>
        AVRO,

        /// <summary>Protocol Buffers serialization (not yet implemented).</summary>
        PROTOBUF
    }
}
