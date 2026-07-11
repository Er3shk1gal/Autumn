# Configuration

All configuration goes through `IrkallaKafkaOptions` in `AddIrkallaKafka(...)`.

## Options reference

| Option | Type | Default | Description |
|---|---|---|---|
| `BootstrapServers` | `string` | `"localhost:9092"` | Kafka bootstrap servers (comma-separated) |
| `GroupId` | `string` | *(required)* | Consumer group id |
| `ServiceName` | `string?` | `null` | Logical name for the outgoing `sender` header |
| `SchemaRegistryUrl` | `string?` | `null` | Schema Registry URL (required for AVRO/PROTOBUF) |
| `AutoCreateTopics` | `bool` | `true` | Auto-create topics that do not exist |
| `ErrorPolicy` | `ErrorPolicy` | `Dlq` | Poison-message policy: `Skip` / `Dlq` / `Stop` |
| `MaxRetries` | `int` | `3` | Retry attempts for transient errors |
| `RetryDelay` | `TimeSpan` | `1s` | Base delay for exponential backoff |
| `MaxRetryDelay` | `TimeSpan` | `30s` | Cap on a single backoff delay |
| `DlqTopicSuffix` | `string` | `".dlq"` | Suffix for the DLQ topic |
| `ConsumerMode` | `ConsumerMode` | `Single` | `Single` (1 consumer/topic) or `Auto` (scale to partitions) |
| `MaxConsumersPerTopic` | `int` | `0` | Cap for `Auto` (0 = partition count) |
| `AutoOffsetReset` | `AutoOffsetReset` | `Earliest` | Offset reset policy |
| `JsonSerializerOptions` | `JsonSerializerOptions` | `Web` | JSON serialization settings |
| `ServiceAssembly` | `Assembly?` | `null` (entry assembly) | Assembly to scan for `[KafkaService]` |
| `Security` | `KafkaSecurityOptions` | `new()` | First-class SSL/SASL — see [Security](Security-and-Kafka-Configuration) |
| `RawConfig` | `Dictionary<string,string>` | `{}` | Raw `librdkafka` key/values (any setting) |
| `ConfigureConsumer` | `Action<ConsumerConfig>?` | `null` | Advanced consumer override (applied last) |
| `ConfigureProducer` | `Action<ProducerConfig>?` | `null` | Advanced producer override (applied last) |
| `ConfigureAdminClient` | `Action<AdminClientConfig>?` | `null` | Advanced admin override (applied last) |
| `ConfigureSchemaRegistry` | `Action<SchemaRegistryConfig>?` | `null` | Schema Registry override (auth/SSL) |

## `[KafkaService]`

| Property | Type | Default | Description |
|---|---|---|---|
| `requestTopic` | `string` | *(required)* | Topic to consume from |
| `serviceName` | `string` | *(required)* | Service identifier in headers |
| `RequestPartitions` | `int` | `1` | Partitions for the request topic (cap for `Auto`) |
| `HandlerType` | `MessageHandlerType` | `JSON` | `JSON` / `AVRO` / `PROTOBUF` |
| `ResponseTopic` | `string?` | `null` | Topic for responses |
| `ResponsePartitions` | `int` | `1` | Partitions for the response topic |
| `DefaultResponsePartition` | `int` | `0` | Default response partition |
| `RequestReplicationFactor` | `short` | `1` | RF for the request topic |
| `ResponseReplicationFactor` | `short` | `1` | RF for the response topic |

## `[KafkaMethod]`

| Property | Type | Default | Description |
|---|---|---|---|
| `methodName` | `string` | *(required)* | Value matched against the `method` header |
| `RequiresResponse` | `bool` | `false` | Publish a response to `ResponseTopic` |
| `ResponsePartition` | `int` | `-1` | Override response partition (`-1` = service default) |

## Full example

```csharp
builder.Services.AddIrkallaKafka(o =>
{
    o.BootstrapServers = "broker1:9092,broker2:9092";
    o.GroupId = "billing";
    o.ServiceName = "billing-service";
    o.ServiceAssembly = typeof(Program).Assembly;

    o.AutoOffsetReset = AutoOffsetReset.Earliest;
    o.ErrorPolicy = ErrorPolicy.Dlq;
    o.MaxRetries = 3;
    o.RetryDelay = TimeSpan.FromSeconds(1);

    o.ConsumerMode = ConsumerMode.Auto;   // parallelize per partition

    o.JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
});
```

## Notes

- **`GroupId` is required.** It determines offset ownership and consumer-group identity.
- **`EnableAutoCommit` is not configurable** — Irkalla commits manually after successful
  processing (or after DLQ publish) to guarantee at-least-once delivery. Trying to enable it (even
  via `RawConfig` or a `Configure*` callback) throws a `KafkaConfigurationException`.
- Multiple `[KafkaService]` classes may **share a request topic** — they merge into one consumer,
  and each method name must be unique on that topic.
