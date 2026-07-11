# Observability

Irkalla emits an `ActivitySource` and a `Meter`, both named **`Irkalla.Kafka`**. No extra
dependencies — wire them into OpenTelemetry (or any listener) if you want traces and metrics.

## Tracing

Each processed message starts an `irkalla.kafka.consume` consumer activity with tags:

- `messaging.system = kafka`
- `messaging.destination.name = <topic>`
- `messaging.kafka.message.key`
- `method`

W3C trace context (`traceparent` / `tracestate`) is read from the incoming message and injected
into the response and DLQ messages, so a distributed trace flows **producer → consumer → response**.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource("Irkalla.Kafka")
        .AddOtlpExporter());
```

## Metrics

| Instrument | Type | Meaning |
|---|---|---|
| `messages_processed` | counter | successfully processed messages |
| `messages_failed` | counter | messages that exhausted retries |
| `messages_dlq` | counter | messages published to a DLQ |
| `retry_attempts` | counter | retry attempts |
| `processing_duration` | histogram (ms) | per-message processing time |

Each is tagged with `topic`.

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddMeter("Irkalla.Kafka")
        .AddOtlpExporter());
```

## Logging

Irkalla logs through `ILogger` (Microsoft.Extensions.Logging). Key events: consumer start/stop,
subscription, retries (warning), DLQ (warning), poison/commit/broker errors (error/critical). Set
the log level for the `Irkalla.Kafka.*` categories as usual.
