using Irkalla.Kafka.Attributes;
using Irkalla.Kafka.Extensions;
using Irkalla.Kafka.HealthChecks;
using Irkalla.Kafka.Producing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Irkalla.Kafka sample — a service that consumes, plus a producer that sends.
// Requires a Kafka broker on localhost:9092 (e.g. `docker run -p 9092:9092 apache/kafka`).

var builder = Host.CreateApplicationBuilder(args);

// The handler class is resolved from DI per message.
builder.Services.AddScoped<OrderService>();

// Consumers + a typed IKafkaProducer, configured from appsettings or code.
builder.Services.AddIrkallaKafka(o =>
{
    o.BootstrapServers = "localhost:9092";
    o.GroupId = "irkalla-samples";
    o.ServiceName = "samples";
    o.ServiceAssembly = typeof(Program).Assembly;
});

// Optional: expose consumer health.
builder.Services.AddHealthChecks().AddCheck<IrkallaKafkaHealthCheck>("kafka");

// A tiny background sender that fires a request every few seconds.
builder.Services.AddHostedService<DemoSender>();

await builder.Build().RunAsync();

// ── A Kafka service: methods are routed by the "method" header ──
[KafkaService("orders-request", "order-service", ResponseTopic = "orders-response")]
public class OrderService
{
    // request-response: the return value is published to the response topic
    [KafkaMethod("CreateOrder", RequiresResponse = true)]
    public OrderResult CreateOrder(CreateOrderRequest req)
    {
        Console.WriteLine($"[consumer] CreateOrder: {req.Item}");
        return new OrderResult(Guid.NewGuid().ToString("N"), req.Item);
    }

    // fire-and-forget: no response
    [KafkaMethod("CancelOrder")]
    public Task CancelOrder(CancelOrderCommand cmd, CancellationToken ct)
    {
        Console.WriteLine($"[consumer] CancelOrder: {cmd.OrderId}");
        return Task.CompletedTask;
    }
}

// ── A producer using IKafkaProducer.SendAsync ──
public class DemoSender(IKafkaProducer producer) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await producer.SendAsync("orders-request", "CreateOrder",
                new CreateOrderRequest("widget"), key: "cust-1", cancellationToken: stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

public record CreateOrderRequest(string Item);
public record OrderResult(string OrderId, string Item);
public record CancelOrderCommand(string OrderId);
