using System.Linq;
using System.Text;
using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Producing;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Irkalla.Kafka.Tests;

public class ProducerUnitTests
{
    private static string? Header(Message<string, byte[]> m, string key)
    {
        var h = m.Headers.FirstOrDefault(x => x.Key == key);
        return h == null ? null : Encoding.UTF8.GetString(h.GetValueBytes());
    }

    [Fact]
    public async Task SendAsync_Builds_Message_With_Headers_And_Json()
    {
        Message<string, byte[]>? captured = null;
        string? capturedTopic = null;

        var mock = new Mock<IProducer<string, byte[]>>();
        mock.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, byte[]>, CancellationToken>((t, m, _) => { capturedTopic = t; captured = m; })
            .ReturnsAsync(new DeliveryResult<string, byte[]> { Status = PersistenceStatus.Persisted });

        var options = new IrkallaKafkaOptions { ServiceName = "svc" };
        var producer = new KafkaMessageProducer(mock.Object, options, NullLogger<KafkaMessageProducer>.Instance);

        await producer.SendAsync("orders-request", "CreateOrder", new { Name = "abc" },
            key: "k1", correlationId: "c1", messageId: "m1",
            headers: new Dictionary<string, string> { ["tenant"] = "t7" });

        Assert.Equal("orders-request", capturedTopic);
        Assert.NotNull(captured);
        Assert.Equal("k1", captured!.Key);
        Assert.Equal("CreateOrder", Header(captured, "method"));
        Assert.Equal("svc", Header(captured, "sender"));
        Assert.Equal("c1", Header(captured, "correlation-id"));
        Assert.Equal("m1", Header(captured, "message-id"));
        Assert.Equal("t7", Header(captured, "tenant"));
        Assert.Contains("\"name\":\"abc\"", Encoding.UTF8.GetString(captured.Value)); // Web JSON = camelCase
    }

    [Fact]
    public async Task SendAsync_Omits_Optional_Headers_When_Not_Provided()
    {
        Message<string, byte[]>? captured = null;
        var mock = new Mock<IProducer<string, byte[]>>();
        mock.Setup(p => p.ProduceAsync(It.IsAny<string>(), It.IsAny<Message<string, byte[]>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Message<string, byte[]>, CancellationToken>((_, m, _) => captured = m)
            .ReturnsAsync(new DeliveryResult<string, byte[]> { Status = PersistenceStatus.Persisted });

        var producer = new KafkaMessageProducer(mock.Object, new IrkallaKafkaOptions(), NullLogger<KafkaMessageProducer>.Instance);
        await producer.SendAsync("t", "M", 123);

        Assert.Equal("M", Header(captured!, "method"));
        Assert.Null(Header(captured!, "sender"));          // no ServiceName
        Assert.Null(Header(captured!, "correlation-id"));
        Assert.Null(Header(captured!, "message-id"));
    }

    [Theory]
    [InlineData("", "m")]
    [InlineData("t", "")]
    public async Task SendAsync_Validates_Topic_And_Method(string topic, string method)
    {
        var producer = new KafkaMessageProducer(
            Mock.Of<IProducer<string, byte[]>>(), new IrkallaKafkaOptions(), NullLogger<KafkaMessageProducer>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => producer.SendAsync(topic, method, 1));
    }
}
