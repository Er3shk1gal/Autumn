# Irkalla.Kafka — Sample

A minimal console app showing Irkalla.Kafka end-to-end:

- an `OrderService` with a **request-response** method (`CreateOrder`) and a **fire-and-forget**
  method (`CancelOrder`);
- registration via `AddIrkallaKafka`;
- a consumer health check;
- a `DemoSender` background service that produces requests with `IKafkaProducer.SendAsync`.

## Run

Start a broker on `localhost:9092`, e.g.:

```bash
docker run -p 9092:9092 apache/kafka:latest
```

then:

```bash
dotnet run
```

The sender fires a `CreateOrder` every 5 seconds; the consumer prints it and publishes a response to
`orders-response`.
