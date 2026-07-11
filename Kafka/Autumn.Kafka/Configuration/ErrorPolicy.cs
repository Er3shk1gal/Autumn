namespace Autumn.Kafka.Configuration;

/// <summary>
/// Defines how the framework handles poison messages (errors during deserialization, missing headers, or business logic exceptions).
/// </summary>
public enum ErrorPolicy
{
    /// <summary>
    /// Skips the message and continues processing. The offset is committed.
    /// </summary>
    Skip,
    
    /// <summary>
    /// Skips the message, but publishes it to a Dead-Letter Queue (DLQ) topic before committing the offset.
    /// </summary>
    Dlq,
    
    /// <summary>
    /// Stops the consumer and faults the application. The offset is NOT committed.
    /// </summary>
    Stop
}
