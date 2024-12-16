namespace Autumn.Kafka.Exceptions.TopicExceptions;

public class CheckTopicException : TopicException
{
    public CheckTopicException()
    {
    }

    public CheckTopicException(string message)
        : base(message)
    {
    }

    public CheckTopicException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}