# Irkalla.Kafka

**Attribute-based Kafka framework for .NET — write Kafka consumers like ASP.NET controllers.**

Named after *Irkalla*, the Mesopotamian underworld: messages pass through its gate, routed by a gatekeeper to the right handler.

```csharp
[KafkaService("orders-request", "order-service", ResponseTopic = "orders-response")]
public class OrderService
{
    [KafkaMethod("CreateOrder", RequiresResponse = true)]
    public OrderResult CreateOrder(CreateOrderRequest req) => /* ... */;
}
```

## Why Irkalla.Kafka

- **Attribute routing** — `[KafkaService]` / `[KafkaMethod]`, dispatched by a message header. No hand-written consume loops.
- **JSON / Avro / Protobuf** handlers (Schema Registry for the binary formats).
- **At-least-once delivery** — manual offset commit, clamped exponential retry, dead-letter queue.
- **Auto-scaling consumers** — one consumer per topic, or several per topic (`ConsumerMode.Auto`).
- **Full Kafka flexibility** — first-class SSL/SASL options plus raw passthrough to every `librdkafka` setting.
- **Observability** — OpenTelemetry `ActivitySource` + metrics out of the box.

## Install

```bash
dotnet add package Irkalla.Kafka
```

## Pages

- **[Getting Started](Getting-Started)** — first service in 5 minutes.
- **[Configuration](Configuration)** — every option explained.
- **[Consumer Modes](Consumer-Modes)** — Single vs Auto, scaling throughput.
- **[Error Handling & DLQ](Error-Handling-and-DLQ)** — retries, policies, poison messages.
- **[Security & Kafka Configuration](Security-and-Kafka-Configuration)** — SSL/SASL, raw passthrough, precedence.
- **[Serialization](Serialization)** — JSON / Avro / Protobuf.
- **[Observability](Observability)** — tracing and metrics.
- **[FAQ & Troubleshooting](FAQ)** — common issues.

## License

Apache-2.0
