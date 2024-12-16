namespace Autumn.Kafka.Exceptions.ProducerExceptions;

public class ConsumerException : KafkaException
{
    public ConsumerException()
    {
    }

    public ConsumerException(string message)
        : base(message)
    {
    }

    public ConsumerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}