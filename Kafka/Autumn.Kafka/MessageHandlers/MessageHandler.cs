namespace Autumn.Kafka.MessageHandlers
{
    public abstract class MessageHandler
    {
        public abstract Task Consume(CancellationToken cancellationToken = default);
    }
}