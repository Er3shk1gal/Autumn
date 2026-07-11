# Irkalla.Kafka

Attribute-based Kafka framework for .NET (net8.0 / net10.0) — write Kafka consumers like ASP.NET controllers.

- **Attribute routing** — `[KafkaService]` / `[KafkaMethod]`, dispatched by a message header.
- **Fire-and-forget or request-response** — responses are opt-in per method.
- **Typed producer** — `IKafkaProducer.SendAsync<T>(...)`, plus producer-only registration (no `GroupId`).
- **Request-reply RPC** — `IKafkaRpcClient.CallAsync<TReq, TRes>(...)`, correlation-matched typed replies.
- **JSON / Avro / Protobuf** handlers (Schema Registry for the binary formats).
- **At-least-once delivery** — manual offset commit, exponential retry, and a dead-letter queue.
- **Auto-scaling consumers** — one consumer per topic, or several per topic (`ConsumerMode.Auto`).
- **Full Kafka flexibility** — first-class SSL/SASL options plus raw passthrough to every `librdkafka` setting.
- **Configuration** — code, `appsettings.json` (`IConfiguration`), or `IOptions`.
- **Observability** — OpenTelemetry `ActivitySource` + metrics, and a health check, out of the box.

## Quick Start

### 1. Install

```bash
dotnet add package Irkalla.Kafka
```

### 2. Register in DI

```csharp
// Program.cs
builder.Services.AddIrkallaKafka(options =>
{
    options.BootstrapServers = "localhost:9092";
    options.GroupId = "my-consumer-group";
    options.ServiceName = "order-service";
    options.ServiceAssembly = typeof(Program).Assembly;
});
```

### 3. Create a Kafka Service

```csharp
using Irkalla.Kafka.Attributes;

[KafkaService("orders-request", "order-service", ResponseTopic = "orders-response")]
public class OrderKafkaService
{
    private readonly IOrderRepository _repository;

    public OrderKafkaService(IOrderRepository repository) => _repository = repository;

    [KafkaMethod("CreateOrder", RequiresResponse = true)]
    public OrderResult CreateOrder(CreateOrderRequest request) => _repository.Create(request);

    [KafkaMethod("GetOrder", RequiresResponse = true)]
    public Order? GetOrder(GetOrderQuery query) => _repository.GetById(query.Id);

    [KafkaMethod("DeleteOrder")]
    public async Task DeleteOrder(DeleteOrderCommand command, CancellationToken ct)
        => await _repository.DeleteAsync(command.Id, ct);
}
```

A handler method takes **one payload parameter** (deserialized from the message body) plus an
optional `CancellationToken`. It may return a value, `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`.

### 4. Register your service in DI

```csharp
builder.Services.AddScoped<OrderKafkaService>();
// or by interface: builder.Services.AddScoped<IOrderKafkaService, OrderKafkaService>();
```

Consumers start automatically with the host.

## How It Works

1. `AddIrkallaKafka()` scans your assembly for `[KafkaService]` classes.
2. For each unique request topic a background consumer is registered (`ConsumerMode.Single`), or
   several consumers per topic in `ConsumerMode.Auto`.
3. Incoming messages are routed by the `"method"` header to the matching `[KafkaMethod]`.
4. The method's payload parameter is deserialized from the message body.
5. If `RequiresResponse = true`, the result is serialized and sent to the response topic.
6. The offset is committed only after successful processing (or after the message is sent to the DLQ).

## Attributes

### `[KafkaService]`

| Property | Type | Default | Description |
|---|---|---|---|
| `requestTopic` | `string` | *(required)* | Topic to consume from |
| `serviceName` | `string` | *(required)* | Service identifier in message headers |
| `GroupId` | `string?` | `null` | Per-service consumer group override (default: global `GroupId`) |
| `RequestPartitions` | `int` | `1` | Partitions for the request topic (also the cap for `ConsumerMode.Auto`) |
| `HandlerType` | `MessageHandlerType` | `JSON` | Serialization format (JSON / AVRO / PROTOBUF) |
| `ResponseTopic` | `string?` | `null` | Topic for responses |
| `ResponsePartitions` | `int` | `1` | Partitions for the response topic |
| `DefaultResponsePartition` | `int` | `0` | Default partition for responses |
| `RequestReplicationFactor` | `short` | `1` | Replication factor for the request topic |
| `ResponseReplicationFactor` | `short` | `1` | Replication factor for the response topic |

### `[KafkaMethod]`

| Property | Type | Default | Description |
|---|---|---|---|
| `methodName` | `string` | *(required)* | Method identifier in the `method` header |
| `RequiresResponse` | `bool` | `false` | Whether to send a response |
| `ResponsePartition` | `int` | `-1` | Override response partition (`-1` = service default) |

## Configuration Options

| Option | Type | Default | Description |
|---|---|---|---|
| `BootstrapServers` | `string` | `"localhost:9092"` | Kafka bootstrap servers |
| `GroupId` | `string` | *(required)* | Consumer group id |
| `ServiceName` | `string?` | `null` | Logical name for the outgoing `sender` header |
| `SchemaRegistryUrl` | `string?` | `null` | Schema Registry URL (required for AVRO/PROTOBUF) |
| `AutoCreateTopics` | `bool` | `true` | Auto-create topics that do not exist |
| `ErrorPolicy` | `ErrorPolicy` | `Dlq` | Poison-message policy (Skip / Dlq / Stop) |
| `MaxRetries` | `int` | `3` | Retry attempts for transient errors |
| `RetryDelay` | `TimeSpan` | `1s` | Base delay for exponential backoff |
| `MaxRetryDelay` | `TimeSpan` | `30s` | Cap on a single backoff delay (avoids `max.poll.interval.ms` eviction) |
| `EnableIdempotence` | `bool` | `true` | Idempotent producer — no duplicate produces on retry |
| `DlqTopicSuffix` | `string` | `".dlq"` | Suffix for the DLQ topic |
| `IncludeStackTraceInDlq` | `bool` | `false` | Include the exception stack trace in DLQ headers (off — avoids leaking internals) |
| `ConsumerMode` | `ConsumerMode` | `Single` | `Single` (1 consumer/topic) or `Auto` (scale to partitions) |
| `MaxConsumersPerTopic` | `int` | `0` | Cap for `Auto` (0 = partition count) |
| `AutoOffsetReset` | `AutoOffsetReset` | `Earliest` | Offset reset policy |
| `JsonSerializerOptions` | `JsonSerializerOptions` | `Web` | JSON serialization settings |
| `ServiceAssembly` | `Assembly?` | `null` | Assembly to scan for services |
| `ServiceAssemblies` | `Assembly[]?` | `null` | Multiple assemblies to scan (merged; wins over `ServiceAssembly`) |
| `Security` | `KafkaSecurityOptions` | `new()` | First-class SSL/SASL settings (see below) |
| `RawConfig` | `Dictionary<string,string>` | `{}` | Raw `librdkafka` key/values (any setting) |
| `ConfigureConsumer` | `Action<ConsumerConfig>?` | `null` | Advanced consumer override (applied last) |
| `ConfigureProducer` | `Action<ProducerConfig>?` | `null` | Advanced producer override (applied last) |
| `ConfigureAdminClient` | `Action<AdminClientConfig>?` | `null` | Advanced admin override (applied last) |
| `ConfigureSchemaRegistry` | `Action<SchemaRegistryConfig>?` | `null` | Schema Registry override (auth/SSL) |

## Consumer Scaling

- **`ConsumerMode.Single`** (default) — one consumer per topic. Messages on a topic are processed
  sequentially; a slow handler never affects other topics. Scale out by running more app instances.
- **`ConsumerMode.Auto`** — several consumers in the same group per topic, up to the partition count
  (optionally capped by `MaxConsumersPerTopic`). Kafka spreads partitions across them for in-process
  parallelism while preserving per-partition ordering.

```csharp
options.ConsumerMode = ConsumerMode.Auto;   // parallelize bottlenecked handlers
options.MaxConsumersPerTopic = 4;           // optional cap
```

> A topic must actually have multiple partitions for `Auto` to parallelize. Set `RequestPartitions`
> accordingly, and make sure the topic is created with that partition count.

## Security & Full Kafka Configuration

The TLS/SASL handshake is performed by `librdkafka` (Confluent.Kafka) — Irkalla.Kafka forwards your
settings and never hides a `librdkafka` option. Configuration is layered, last writer wins:

**defaults → typed `Security` → `RawConfig` → `Configure*` callback**

```csharp
services.AddIrkallaKafka(options =>
{
    options.BootstrapServers = "broker:9093";
    options.GroupId = "svc";

    // Typed SSL/SASL (applied to consumer, producer AND admin clients)
    options.Security.SecurityProtocol = SecurityProtocol.SaslSsl;
    options.Security.SaslMechanism = SaslMechanism.Plain;
    options.Security.SaslUsername = "user";
    options.Security.SaslPassword = "password";
    options.Security.SslCaLocation = "/certs/ca.pem";

    // Any librdkafka setting not surfaced as a typed option
    options.RawConfig["fetch.max.bytes"] = "5242880";

    // Full escape hatch — overrides everything above
    options.ConfigureConsumer = c => c.ClientId = "svc-1";
});
```

The only deliberate restriction is `EnableAutoCommit`: it is forced off (and rejected even via
`RawConfig`) because Irkalla.Kafka commits manually to guarantee at-least-once delivery.

## Error Handling & Retries

Transient exceptions are retried up to `MaxRetries` times with clamped exponential backoff
(`min(RetryDelay * 2^(attempt-1), MaxRetryDelay)`).

> [!IMPORTANT]
> With `MaxRetries > 0`, delivery is at-least-once — design your handlers to be idempotent.

**Deterministic errors** (`KafkaConsumerException` / `KafkaConfigurationException` — bad payload,
missing `method` header, unknown method) skip retries and apply the error policy immediately.

**Error policies:**
- **`Dlq`** (default) — publish to `<request-topic>.dlq` with `error` + `stacktrace` headers, then commit.
- **`Skip`** — log a warning, skip the message, commit.
- **`Stop`** — log critical and stop the consumer, faulting the host.

Offset-commit and DLQ-publish failures are handled without crashing the consumer (the message is
redelivered rather than lost).

## Producing Messages

Send to Irkalla services with the typed producer — `AddIrkallaKafka` registers `IKafkaProducer`, and
producer-only apps use `AddIrkallaKafkaProducer` (no `GroupId` needed):

```csharp
// producer-only app
builder.Services.AddIrkallaKafkaProducer(o => o.BootstrapServers = "localhost:9092");

// anywhere
public class OrderApi(IKafkaProducer kafka)
{
    public Task Place(CreateOrderRequest r) => kafka.SendAsync("orders-request", "CreateOrder", r);
}
```

## Request-Reply (RPC)

Send a request and await the typed reply, matched by a `correlation-id` header:

```csharp
builder.Services.AddIrkallaKafkaProducer(o => { o.BootstrapServers = "localhost:9092"; o.GroupId = "web"; });
builder.Services.AddIrkallaKafkaRpcClient();

// ...
public class OrdersApi(IKafkaRpcClient rpc)
{
    public Task<OrderResult?> Create(CreateOrderRequest req) =>
        rpc.CallAsync<CreateOrderRequest, OrderResult>("orders-request", "CreateOrder", req);
}
```

The server just returns a value from its `[KafkaMethod]`. Times out with `KafkaRpcTimeoutException`
(unknown outcome — keep server handlers idempotent). JSON serialization. See the wiki for details.

## Configuration from appsettings.json

```csharp
builder.Services.AddIrkallaKafka(builder.Configuration, o => o.ServiceAssembly = typeof(Program).Assembly);
```
```json
{ "IrkallaKafka": { "BootstrapServers": "broker:9092", "GroupId": "billing", "ConsumerMode": "Auto" } }
```
Code overrides config; `IOptions<IrkallaKafkaOptions>` is registered too.

## Health check

```csharp
builder.Services.AddHealthChecks().AddCheck<IrkallaKafkaHealthCheck>("kafka");
```
Healthy when all consumers run, Degraded while starting, Unhealthy if any faults.

## Message Format

| Header | Description |
|---|---|
| `method` | Maps to `[KafkaMethod("name")]` |
| `sender` | Set from `ServiceName` on responses |
| `traceparent` / `tracestate` | W3C trace context (propagated for distributed tracing) |

Body is serialized with `System.Text.Json` (or Avro/Protobuf if configured).

## Observability

Irkalla.Kafka emits an `ActivitySource` and `Meter` named `"Irkalla.Kafka"`. Register them with
OpenTelemetry to get distributed traces (producer → consumer → response) and metrics
(`messages_processed`, `messages_failed`, `messages_dlq`, `retry_attempts`, `processing_duration`).

## Exceptions

All inherit from `KafkaException`:

| Exception | When |
|---|---|
| `KafkaConfigurationException` | Invalid or missing configuration |
| `KafkaConsumerException` | Message consumption or handler errors |
| `KafkaProducerException` | Message production errors |
| `KafkaTopicException` | Topic operations (create/delete/check) |
| `KafkaServiceResolutionException` | DI resolution or method invocation errors |

## License

Apache-2.0
