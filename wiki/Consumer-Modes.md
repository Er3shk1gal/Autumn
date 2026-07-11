# Consumer Modes

Irkalla runs one background consumer per unique **request topic** by default. `ConsumerMode`
controls whether a topic can be served by more than one consumer for parallelism.

```csharp
options.ConsumerMode = ConsumerMode.Single; // default
// or
options.ConsumerMode = ConsumerMode.Auto;
options.MaxConsumersPerTopic = 4;           // optional cap
```

## `ConsumerMode.Single` (default)

One consumer per topic. Messages on a topic are processed **sequentially** by a single consumer
thread.

- **Isolated** — a slow handler on one topic never affects other topics.
- **Simple** — no in-process coordination.
- **Scale out** by running more instances of your application (they join the same consumer group
  and Kafka splits partitions across the instances).

Best for most services, and for topics where per-message ordering matters and throughput is fine.

## `ConsumerMode.Auto`

Starts **several consumers in the same group** for a topic — up to the topic's partition count
(optionally capped by `MaxConsumersPerTopic`). Kafka's group protocol assigns partitions across
them, giving **in-process parallelism** while preserving per-partition ordering.

Effective consumer count = `min(partitionCount, MaxConsumersPerTopic or partitionCount)`.

- **Parallelizes bottlenecked handlers** — a handler doing real work (DB calls, HTTP, CPU) runs on
  several partitions at once.
- Per-partition ordering is preserved (each partition still has a single owner).
- Costs more threads and broker connections (one consumer each).

### When Auto helps — and when it doesn't

Auto parallelizes across **partitions**. It helps when the **handler** is the bottleneck. For a
trivial handler (a few microseconds), a single consumer already saturates the work and extra
consumers only add coordination overhead.

Measured on a 4-partition topic with a ~2 ms/message handler: **1 consumer ≈ 320 msg/s → 4
consumers ≈ 700 msg/s (~2.1×)**, exactly-once processing preserved.

### Requirement: the topic must actually have multiple partitions

Auto can only scale up to the topic's partition count. Two common gotchas:

- If a **producer touches the topic first**, the broker may auto-create it with `num.partitions=1`
  — then Auto can't scale no matter what. Pre-create the topic with the right partition count, or
  set `RequestPartitions` and let Irkalla create it before anything produces.
- If the topic **already exists with fewer partitions** than `RequestPartitions`, Kafka keeps the
  existing count. Irkalla logs a warning at startup:
  > `Topic 'X' already exists with 1 partition(s), but 4 were requested. ConsumerMode.Auto will run at most 1 effective consumer(s) for this topic.`

## Choosing

| | Single | Auto |
|---|---|---|
| Consumers per topic | 1 | up to partition count |
| Throughput | one thread | scales with partitions |
| Threads / connections | 1 | N |
| Ordering | per partition | per partition |
| Best for | most services; ordering-sensitive, low volume | bottlenecked handlers, high volume |

You can also combine Auto with **horizontal scaling** (more app instances) — Kafka balances
partitions across all consumers in the group, wherever they run.
