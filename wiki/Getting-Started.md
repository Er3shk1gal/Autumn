# Getting Started

## 1. Install

```bash
dotnet add package Irkalla.Kafka
```

## 2. Register in DI

```csharp
// Program.cs
builder.Services.AddIrkallaKafka(options =>
{
    options.BootstrapServers = "localhost:9092";
    options.GroupId = "order-service";
    options.ServiceName = "order-service";
    options.ServiceAssembly = typeof(Program).Assembly;
});
```

`AddIrkallaKafka` scans `ServiceAssembly` for `[KafkaService]` classes and starts one background
consumer per request topic when the host starts.

## 3. Write a service

```csharp
using Irkalla.Kafka.Attributes;

[KafkaService("orders-request", "order-service", ResponseTopic = "orders-response")]
public class OrderKafkaService
{
    private readonly IOrderRepository _repository;

    public OrderKafkaService(IOrderRepository repository) => _repository = repository;

    // Request/response
    [KafkaMethod("CreateOrder", RequiresResponse = true)]
    public OrderResult CreateOrder(CreateOrderRequest request) => _repository.Create(request);

    // Fire-and-forget, async, with cancellation
    [KafkaMethod("DeleteOrder")]
    public async Task DeleteOrder(DeleteOrderCommand command, CancellationToken ct)
        => await _repository.DeleteAsync(command.Id, ct);
}
```

### Handler method rules

- Exactly **one payload parameter** (deserialized from the message body), plus an **optional
  `CancellationToken`**.
- Return type may be a value, `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, or `void`.
- With `RequiresResponse = true`, the returned value is serialized and published to `ResponseTopic`.

## 4. Register the service in DI

```csharp
builder.Services.AddScoped<OrderKafkaService>();
// or by interface:
// builder.Services.AddScoped<IOrderKafkaService, OrderKafkaService>();
```

The handler is resolved from a fresh DI scope **per message**, so scoped dependencies
(e.g. a `DbContext`) work as expected.

## 5. Produce a request

Any Kafka producer can drive it — the only requirement is the `method` header:

```csharp
using var producer = new ProducerBuilder<string, byte[]>(
    new ProducerConfig { BootstrapServers = "localhost:9092" }).Build();

await producer.ProduceAsync("orders-request", new Message<string, byte[]>
{
    Key = orderId,
    Value = JsonSerializer.SerializeToUtf8Bytes(new CreateOrderRequest { /* ... */ }),
    Headers = [ new Header("method", Encoding.UTF8.GetBytes("CreateOrder")) ]
});
```

## How routing works

1. A message arrives on the request topic.
2. Irkalla reads the `method` header and looks up the matching `[KafkaMethod]`.
3. The body is deserialized into the handler's payload parameter.
4. The handler runs inside a DI scope.
5. On success the offset is committed. If `RequiresResponse`, the result is published to the
   response topic with `method` and `sender` headers (and the request `Key` is copied over).

See **[Configuration](Configuration)** for every option and **[Error Handling & DLQ](Error-Handling-and-DLQ)**
for what happens when a handler throws.
