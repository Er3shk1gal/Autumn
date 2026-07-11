# Serialization

Set the format per service with `HandlerType` on `[KafkaService]`.

```csharp
[KafkaService("orders", "svc", HandlerType = MessageHandlerType.JSON)]     // default
[KafkaService("orders", "svc", HandlerType = MessageHandlerType.AVRO)]
[KafkaService("orders", "svc", HandlerType = MessageHandlerType.PROTOBUF)]
```

The payload parameter and the response value are (de)serialized with the chosen format. Both
request and response on a service use the same format.

## JSON (default)

Uses `System.Text.Json`. Tune it globally:

```csharp
options.JsonSerializerOptions = new(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};
```

No Schema Registry required. Any DTO works.

## Avro

Requires a Schema Registry:

```csharp
options.SchemaRegistryUrl = "http://localhost:8081";
```

Payload types are Avro-generated classes (via `avrogen` / `Confluent.SchemaRegistry.Serdes.Avro`).
The serializer/deserializer instances are cached per type.

## Protobuf

Requires a Schema Registry, and each payload type must implement
`Google.Protobuf.IMessage<T>` (i.e. generated from a `.proto`). This is validated at startup — a
non-Protobuf parameter type on a `PROTOBUF` service throws `KafkaConfigurationException`.

```csharp
options.SchemaRegistryUrl = "http://localhost:8081";
```

## Requirements summary

| Format | Schema Registry | Payload type |
|---|---|---|
| JSON | no | any DTO |
| AVRO | **required** | Avro-generated |
| PROTOBUF | **required** | implements `IMessage<T>` |

If a service uses AVRO or PROTOBUF and `SchemaRegistryUrl` is not set, `AddIrkallaKafka` throws at
startup.

## Null / empty responses

A handler that returns `null` (or is `void` / non-generic `Task`) produces an **empty** response
body when `RequiresResponse` is set. For JSON that round-trips to `default`. For Avro/Protobuf,
prefer returning a real message rather than `null`.
