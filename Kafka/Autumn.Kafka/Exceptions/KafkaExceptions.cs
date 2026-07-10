namespace Autumn.Kafka.Exceptions
{
    /// <summary>
    /// Base exception for all Autumn.Kafka errors.
    /// </summary>
    public class KafkaException : Exception
    {
        public KafkaException() { }
        public KafkaException(string message) : base(message) { }
        public KafkaException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when Kafka configuration is invalid or missing.
    /// </summary>
    public class KafkaConfigurationException : KafkaException
    {
        public KafkaConfigurationException() { }
        public KafkaConfigurationException(string message) : base(message) { }
        public KafkaConfigurationException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when an error occurs during message consumption or handler invocation.
    /// </summary>
    public class KafkaConsumerException : KafkaException
    {
        public KafkaConsumerException() { }
        public KafkaConsumerException(string message) : base(message) { }
        public KafkaConsumerException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when an error occurs during message production.
    /// </summary>
    public class KafkaProducerException : KafkaException
    {
        public KafkaProducerException() { }
        public KafkaProducerException(string message) : base(message) { }
        public KafkaProducerException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when a Kafka topic operation fails (create, delete, check).
    /// </summary>
    public class KafkaTopicException : KafkaException
    {
        public KafkaTopicException() { }
        public KafkaTopicException(string message) : base(message) { }
        public KafkaTopicException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Thrown when service resolution or method invocation via reflection fails.
    /// </summary>
    public class KafkaServiceResolutionException : KafkaException
    {
        public KafkaServiceResolutionException() { }
        public KafkaServiceResolutionException(string message) : base(message) { }
        public KafkaServiceResolutionException(string message, Exception innerException) : base(message, innerException) { }
    }
}
