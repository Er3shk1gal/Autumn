# Autumn.Kafka

Attribute-based Kafka framework for .NET — work with Kafka services like with ASP.NET controllers.

## Quick Start

### 1. Install

```bash
dotnet add package Autumn.Kafka
```

### 2. Register in DI

```csharp
// Program.cs
builder.Services.AddAutumnKafka(options =>
{
    options.BootstrapServers = "localhost:9092";
    options.GroupId = "my-consumer-group";
    options.ServiceName = "order-service";
    options.ServiceAssembly = typeof(Program).Assembly;
});
```

### 3. Create a Kafka Service

```csharp
using Autumn.Kafka.Attributes;

[KafkaService("orders-request", "order-service", ResponseTopic = "orders-response")]
public class OrderKafkaService
{
    private readonly IOrderRepository _repository;

    public OrderKafkaService(IOrderRepository repository)
    {
        _repository = repository;
    }

    [KafkaMethod("CreateOrder", RequiresResponse = true)]
    public OrderResult CreateOrder(CreateOrderRequest request)
    {
        return _repository.Create(request);
    }

    [KafkaMethod("GetOrder", RequiresResponse = true)]
    public Order? GetOrder(GetOrderQuery query)
    {
        return _repository.GetById(query.Id);
    }

    [KafkaMethod("DeleteOrder")]
    public void DeleteOrder(DeleteOrderCommand command)
    {
        _repository.Delete(command.Id);
    }
}
```

### 4. Register your service in DI

```csharp
builder.Services.AddScoped<OrderKafkaService>();
// or register by interface:
// builder.Services.AddScoped<IOrderKafkaService, OrderKafkaService>();
```

That's it! The consumers start automatically when the application starts.

## How It Works

1. `AddAutumnKafka()` scans your assembly for classes with `[KafkaService]`
2. For each unique request topic, a separate `IHostedService` consumer is registered
3. Incoming messages are routed by the `"method"` header to the matching `[KafkaMethod]`
4. The method's parameters are deserialized from the message body (JSON)
5. If `RequiresResponse = true`, the result is serialized and sent to the response topic

## Attributes

### `[KafkaService]`

| Property | Type | Default | Description |
|---|---|---|---|
| `requestTopic` | `string` | *(required)* | Topic to consume from |
| `serviceName` | `string` | *(required)* | Service identifier in message headers |
| `RequestPartitions` | `int` | `1` | Number of partitions for request topic |
| `HandlerType` | `MessageHandlerType` | `JSON` | Serialization format |
| `ResponseTopic` | `string?` | `null` | Topic for responses |
| `ResponsePartitions` | `int` | `1` | Partitions for response topic |
| `DefaultResponsePartition` | `int` | `0` | Default partition for responses |
| `RequestReplicationFactor` | `short` | `1` | Replication factor for request topic |
| `ResponseReplicationFactor` | `short` | `1` | Replication factor for response topic |

### `[KafkaMethod]`

| Property | Type | Default | Description |
|---|---|---|---|
| `methodName` | `string` | *(required)* | Method identifier in message headers |
| `Partition` | `int` | `0` | Partition to consume from |
| `RequiresResponse` | `bool` | `false` | Whether to send a response |
| `ResponsePartition` | `int` | `-1` | Override response partition (`-1` = use service default) |

## Configuration

```csharp
services.AddAutumnKafka(options =>
{
    // Required
    options.BootstrapServers = "broker1:9092,broker2:9092";
    options.GroupId = "my-group";

    // Optional
    options.ServiceName = "my-service";
    options.AutoCreateTopics = true;
    options.AutoOffsetReset = AutoOffsetReset.Earliest;
    options.EnableAutoCommit = false;
    options.ServiceAssembly = typeof(Program).Assembly;

    // Advanced: override consumer/producer configs
    options.ConfigureConsumer = config =>
    {
        config.SessionTimeoutMs = 30000;
        config.MaxPollIntervalMs = 300000;
    };

    options.ConfigureProducer = config =>
    {
        config.Acks = Acks.All;
        config.LingerMs = 5;
    };
});
```

## Multi-Topic Example

Each `[KafkaService]` class gets its own consumer with isolated lifecycle:

```csharp
[KafkaService("orders-topic", "order-svc", ResponseTopic = "orders-response")]
public class OrderService
{
    [KafkaMethod("Create", RequiresResponse = true)]
    public OrderResult Create(CreateOrderRequest req) { /* ... */ }
}

[KafkaService("payments-topic", "payment-svc", RequestPartitions = 3)]
public class PaymentService
{
    [KafkaMethod("Process", Partition = 0)]
    public void ProcessCard(CardPayment payment) { /* ... */ }

    [KafkaMethod("Refund", Partition = 1)]
    public void Refund(RefundRequest req) { /* ... */ }
}
```

This creates **two independent consumers** — one for `orders-topic`, one for `payments-topic`.

## Message Format

Messages are routed by the `method` header:

| Header | Description |
|---|---|
| `method` | Maps to `[KafkaMethod("name")]` |
| `sender` | Set automatically from `ServiceName` in responses |

Message body is JSON-serialized using Newtonsoft.Json.

## Exceptions

All exceptions inherit from `KafkaException`:

| Exception | When |
|---|---|
| `KafkaConfigurationException` | Invalid or missing configuration |
| `KafkaConsumerException` | Message consumption or handler errors |
| `KafkaProducerException` | Message production errors |
| `KafkaTopicException` | Topic operations (create/delete/check) |
| `KafkaServiceResolutionException` | DI resolution or method invocation errors |

## License

Apache-2.0