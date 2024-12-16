namespace Autumn.Kafka.Exceptions.ProducerExceptions;

public class ConsumerRecievedMessageInvalidException : ConsumerException
{
    public ConsumerRecievedMessageInvalidException()
    {
    }

    public ConsumerRecievedMessageInvalidException(string message)
        : base(message)
    {
    }

    public ConsumerRecievedMessageInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}