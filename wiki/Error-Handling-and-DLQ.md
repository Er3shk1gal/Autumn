# Error Handling & DLQ

Irkalla delivers **at-least-once**: the offset is committed only after a message is successfully
processed, or after it has been sent to the dead-letter queue.

## Retries

When a handler throws a **transient** exception, the message is retried up to `MaxRetries` times
with clamped exponential backoff:

```
delay = min(RetryDelay * 2^(attempt - 1), MaxRetryDelay)
```

Defaults: `MaxRetries = 3`, `RetryDelay = 1s`, `MaxRetryDelay = 30s` → delays 1s, 2s, 4s.

> **Handlers should be idempotent when `MaxRetries > 0`.** A retry re-invokes the handler, so a
> non-idempotent side effect (e.g. a raw `INSERT`) can run more than once. Use upserts or a
> dedup key. The same applies to at-least-once redelivery after a rebalance.

`MaxRetryDelay` exists so a large `MaxRetries` can't block the consumer poll loop long enough to
trigger a `max.poll.interval.ms` group eviction.

## Deterministic vs transient errors

**Deterministic** errors skip retries and apply the error policy immediately — retrying them is
pointless:

- Missing `method` header
- Unknown method name
- Deserialization failure
- `KafkaConsumerException` / `KafkaConfigurationException`

Everything else (business exceptions from your handler, downstream timeouts) is treated as
**transient** and retried.

## Error policies

Set `options.ErrorPolicy`:

### `ErrorPolicy.Dlq` (default)

After retries are exhausted, publish the message to `<request-topic><DlqTopicSuffix>` (default
`.dlq`) and commit the offset. The DLQ message carries extra headers:

| Header | Value |
|---|---|
| `error` | the exception message |
| `stacktrace` | the exception stack trace |
| (original headers) | preserved |

If the DLQ publish itself fails (broker/DLQ unavailable), Irkalla does **not** commit — the
message is redelivered later rather than lost — and logs the failure without crashing the consumer.

### `ErrorPolicy.Skip`

Log a warning, skip the message, commit the offset. The message is dropped.

### `ErrorPolicy.Stop`

Log critical and stop the consumer, faulting the host (via `BackgroundServiceExceptionBehavior`).
Use when any unhandled message should halt the service for investigation.

## Consuming the DLQ

A DLQ topic is a normal Kafka topic. Reprocess it with a dedicated `[KafkaService]`, or inspect it
with any consumer:

```csharp
[KafkaService("orders-request.dlq", "orders-dlq-handler")]
public class OrdersDlqHandler
{
    [KafkaMethod("CreateOrder")]
    public void Inspect(CreateOrderRequest req) { /* alert, store, replay... */ }
}
```

## Broker and commit failures

- A **transient** `ConsumeException` (e.g. broker restart) is logged and the loop continues —
  librdkafka reconnects automatically. Only a **fatal** `ConsumeException` stops the consumer.
- A failed **offset commit** (e.g. after a rebalance) is logged and does not crash the consumer;
  the message may be redelivered to the new partition owner.

## Guarantees at a glance

| Situation | Behavior |
|---|---|
| Handler succeeds | commit |
| Transient error | retry × `MaxRetries`, then policy |
| Deterministic error | policy immediately (no retry) |
| DLQ publish fails | no commit → redelivery |
| Broker blips | consumer survives, reconnects |
| `Stop` policy | host stops |
