# FAQ & Troubleshooting

## The consumer doesn't receive anything

- Is the `method` header set on the produced message, and does it match a `[KafkaMethod("…")]`?
  A missing/unknown method is a deterministic error → it goes straight to the error policy (DLQ by
  default).
- Is the service registered in DI (`AddScoped<MyService>()`)?
- Is `ServiceAssembly` the assembly that contains your `[KafkaService]` classes?
- Is `AutoOffsetReset` what you expect? Default `Earliest` reads from the beginning for a new group.

## `AddIrkallaKafka` throws at startup

Startup validation is intentional. Common causes:

- `GroupId` is empty — it's required.
- A `[KafkaService]` has no `[KafkaMethod]` methods.
- Two methods share a `MethodName` on the same topic.
- Services on the same topic declare different `HandlerType`.
- A method requires a response but the service has no `ResponseTopic`.
- A method has more than one payload parameter.
- `PROTOBUF` payload type doesn't implement `IMessage<T>`.
- `AVRO`/`PROTOBUF` used without `SchemaRegistryUrl`.

## `ConsumerMode.Auto` doesn't speed anything up

- The topic must have **multiple partitions**. If a producer auto-created it with
  `num.partitions=1`, or it pre-exists with fewer partitions than `RequestPartitions`, Auto can't
  scale (watch for the startup warning). Pre-create the topic with the right partition count.
- Auto parallelizes across partitions — it only helps when the **handler** is the bottleneck. A
  trivial handler is already saturated by one consumer. See [Consumer Modes](Consumer-Modes).

## I get duplicate side effects

Delivery is **at-least-once**. A message can be reprocessed after a rebalance, a commit failure, or
a retry. Make handlers idempotent (upsert, dedup key). This is inherent to Kafka's at-least-once
model, not specific to Irkalla.

## How do I connect to a secured / Confluent Cloud cluster?

Set `options.Security` (SASL/SSL). See
[Security & Kafka Configuration](Security-and-Kafka-Configuration).

## Can I set a librdkafka option that isn't exposed?

Yes — `options.RawConfig["some.librdkafka.key"] = "value"`, or a `Configure*` callback. Nothing is
locked away except `enable.auto.commit`.

## The package isn't in my GitHub repo's "Packages" list

`nuget.org` and **GitHub Packages** are different registries. Publishing to nuget.org (the public
gallery) does not add the package to a repo's GitHub Packages sidebar. To also publish to GitHub
Packages, push to `https://nuget.pkg.github.com/<owner>/index.json` with a GitHub token — but for a
public library, nuget.org is the canonical home.

## Can a plain producer (no consumers) use this?

Today the framework is consumer-first: `AddIrkallaKafka` scans for `[KafkaService]` and requires a
`GroupId`. A dedicated producer-only API is planned. For now you can resolve and use the underlying
`Confluent.Kafka` producer directly.

## Graceful shutdown

On host stop, each consumer finishes its current message, commits, closes the consumer (leaving the
group cleanly), and disposes. No special handling needed.
