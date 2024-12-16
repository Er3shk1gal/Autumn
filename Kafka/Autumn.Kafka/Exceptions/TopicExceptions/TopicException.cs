namespace Autumn.Kafka.Exceptions.TopicExceptions;

public class TopicException : KafkaException
{
    public TopicException()
    {
    }

    public TopicException(string message)
        : base(message)
    {
    }

    public TopicException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}