using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Utils;
using Irkalla.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Irkalla.Kafka.MessageHandlers
{
    /// <summary>
    /// Message handler that deserializes JSON messages, routes them by "method" header
    /// to the appropriate service method, and optionally sends JSON responses.
    /// </summary>
    public class JsonMessageHandler(
        KafkaProducer producer,
        IConsumer<string, byte[]> consumer,
        MessageHandlerConfig messageHandlerConfig,
        ILogger<JsonMessageHandler> logger,
        IServiceProvider serviceProvider,
        IrkallaKafkaOptions options)
        : BaseMessageHandler(producer, consumer, messageHandlerConfig, logger, serviceProvider, options)
    {
        protected override Task<object?> DeserializeAsync(byte[] bytes, Type targetType, Confluent.Kafka.SerializationContext context)
        {
            return Task.FromResult(JsonSerializer.Deserialize(bytes, targetType, Options.JsonSerializerOptions));
        }

        protected override Task<byte[]> SerializeAsync(object obj, Type targetType, Confluent.Kafka.SerializationContext context)
        {
            return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(obj, targetType, Options.JsonSerializerOptions));
        }
    }
}