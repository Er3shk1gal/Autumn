namespace Autumn.Kafka.Exceptions.ConsumerExceptions;

public class ConsumerReceivedMessageInvalidException : ConsumerException
{
    public ConsumerReceivedMessageInvalidException()
    {
    }

    public ConsumerReceivedMessageInvalidException(string message)
        : base(message)
    {
    }

    public ConsumerReceivedMessageInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}