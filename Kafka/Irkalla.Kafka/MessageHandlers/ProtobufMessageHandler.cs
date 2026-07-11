using System.Collections.Concurrent;
using Irkalla.Kafka.Utils;
using Irkalla.Kafka.Utils.Models;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Microsoft.Extensions.Logging;

namespace Irkalla.Kafka.MessageHandlers
{
    public class ProtobufMessageHandler(
        KafkaProducer producer,
        IConsumer<string, byte[]> consumer,
        MessageHandlerConfig messageHandlerConfig,
        ILogger<ProtobufMessageHandler> logger,
        IServiceProvider serviceProvider,
        ISchemaRegistryClient schemaRegistryClient,
        Irkalla.Kafka.Configuration.IrkallaKafkaOptions options)
        : BaseMessageHandler(producer, consumer, messageHandlerConfig, logger, serviceProvider, options)
    {
        private readonly ConcurrentDictionary<Type, Func<byte[], SerializationContext, Task<object?>>> _deserializers = new();
        private readonly ConcurrentDictionary<Type, Func<object, SerializationContext, Task<byte[]>>> _serializers = new();
        private readonly ISchemaRegistryClient _schemaRegistryClient = schemaRegistryClient;

        protected override Task<object?> DeserializeAsync(byte[] bytes, Type targetType, SerializationContext context)
        {
            var deserializer = _deserializers.GetOrAdd(targetType, t =>
            {
                var deserializerType = typeof(Confluent.SchemaRegistry.Serdes.ProtobufDeserializer<>).MakeGenericType(t);
                var instance = Activator.CreateInstance(deserializerType, _schemaRegistryClient, null);
                var method = deserializerType.GetMethod("DeserializeAsync")!;

                return async (b, ctx) =>
                {
                    var memory = new ReadOnlyMemory<byte>(b);
                    var task = (Task)method.Invoke(instance, [memory, b == null, ctx])!;
                    await task;
                    return task.GetType().GetProperty("Result")?.GetValue(task);
                };
            });

            return deserializer(bytes, context);
        }

        protected override Task<byte[]> SerializeAsync(object obj, Type targetType, SerializationContext context)
        {
            var serializer = _serializers.GetOrAdd(targetType, t =>
            {
                var serializerType = typeof(Confluent.SchemaRegistry.Serdes.ProtobufSerializer<>).MakeGenericType(t);
                var instance = Activator.CreateInstance(serializerType, _schemaRegistryClient, null);
                var method = serializerType.GetMethod("SerializeAsync")!;

                return async (o, ctx) =>
                {
                    var task = (Task)method.Invoke(instance, [o, ctx])!;
                    await task;
                    return (byte[])task.GetType().GetProperty("Result")?.GetValue(task)!;
                };
            });

            return serializer(obj, context);
        }
    }
}
