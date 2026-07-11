# Security & Kafka Configuration

Irkalla never hides a `librdkafka` setting. The TLS/SASL handshake is performed by
Confluent.Kafka — Irkalla only forwards your configuration. Everything is layered so you keep
full control.

## Precedence (last writer wins)

```
1. Irkalla defaults
2. Typed properties  (BootstrapServers, GroupId, Security.*)      ← the common 80%
3. RawConfig         (any librdkafka key/value)                   ← escape hatch
4. Configure* callback (ConsumerConfig / ProducerConfig / ...)    ← overrides everything
```

The `Security` block and `RawConfig` are applied to the **consumer, producer, and admin** clients.

## SSL / TLS

```csharp
options.Security.SecurityProtocol = SecurityProtocol.Ssl;
options.Security.SslCaLocation = "/certs/ca.pem";
// mutual TLS:
options.Security.SslCertificateLocation = "/certs/client.pem";
options.Security.SslKeyLocation = "/certs/client.key";
options.Security.SslKeyPassword = "…";
// local/test brokers with self-signed certs only:
options.Security.EnableSslCertificateVerification = false;
```

## SASL (e.g. Confluent Cloud)

```csharp
options.Security.SecurityProtocol = SecurityProtocol.SaslSsl;
options.Security.SaslMechanism = SaslMechanism.Plain;   // or ScramSha256 / ScramSha512
options.Security.SaslUsername = "<api-key>";
options.Security.SaslPassword = "<api-secret>";
```

## Schema Registry auth

```csharp
options.SchemaRegistryUrl = "https://psrc-xxxx.aws.confluent.cloud";
options.ConfigureSchemaRegistry = sr =>
{
    sr.BasicAuthUserInfo = "<sr-key>:<sr-secret>";
};
```

## Raw passthrough — anything else

Any `librdkafka` property not surfaced as a typed option:

```csharp
options.RawConfig["fetch.max.bytes"] = "5242880";
options.RawConfig["socket.keepalive.enable"] = "true";
options.RawConfig["partition.assignment.strategy"] = "cooperative-sticky";
```

## Full escape hatch — the callbacks

Applied last, so they override typed settings and `RawConfig`. Use for anything requiring the
strongly-typed `ConsumerConfig` / `ProducerConfig` / `AdminClientConfig` API:

```csharp
options.ConfigureConsumer = c =>
{
    c.ClientId = "billing-1";
    c.MaxPollIntervalMs = 600_000;
};
options.ConfigureProducer = p => p.LingerMs = 20;
options.ConfigureAdminClient = a => a.ClientId = "billing-admin";
```

## The one deliberate restriction

`EnableAutoCommit` is forced **off** and rejected even via `RawConfig` or `ConfigureConsumer`,
because Irkalla commits manually to guarantee at-least-once delivery. Setting it to `true` throws
`KafkaConfigurationException`. Everything else is fully overridable.

## Complete secured example

```csharp
builder.Services.AddIrkallaKafka(o =>
{
    o.BootstrapServers = "pkc-xxxx.aws.confluent.cloud:9092";
    o.GroupId = "billing";

    o.Security.SecurityProtocol = SecurityProtocol.SaslSsl;
    o.Security.SaslMechanism = SaslMechanism.Plain;
    o.Security.SaslUsername = builder.Configuration["Kafka:ApiKey"];
    o.Security.SaslPassword = builder.Configuration["Kafka:ApiSecret"];

    o.SchemaRegistryUrl = "https://psrc-xxxx.aws.confluent.cloud";
    o.ConfigureSchemaRegistry = sr =>
        sr.BasicAuthUserInfo = builder.Configuration["Kafka:SrAuth"];

    o.RawConfig["client.dns.lookup"] = "use_all_dns_ips";
});
```
