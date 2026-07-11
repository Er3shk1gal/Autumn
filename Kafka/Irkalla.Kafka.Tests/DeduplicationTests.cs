using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Deduplication;
using Xunit;

namespace Irkalla.Kafka.Tests;

public class DeduplicationTests
{
    [Fact]
    public async Task InMemory_Detects_After_Mark_TopicScoped()
    {
        var d = new InMemoryMessageDeduplicator();
        Assert.False(await d.IsDuplicateAsync("m1", "t"));

        await d.MarkProcessedAsync("m1", "t");
        Assert.True(await d.IsDuplicateAsync("m1", "t"));

        // scoped per topic — same id on a different topic is not a duplicate
        Assert.False(await d.IsDuplicateAsync("m1", "other"));
    }

    [Fact]
    public async Task InMemory_Evicts_Oldest_Beyond_Capacity()
    {
        var d = new InMemoryMessageDeduplicator(capacity: 2);
        await d.MarkProcessedAsync("a", "t");
        await d.MarkProcessedAsync("b", "t");
        await d.MarkProcessedAsync("c", "t");   // evicts "a"

        Assert.False(await d.IsDuplicateAsync("a", "t"));
        Assert.True(await d.IsDuplicateAsync("b", "t"));
        Assert.True(await d.IsDuplicateAsync("c", "t"));
    }

    [Fact]
    public void Producer_Idempotence_On_By_Default_And_Toggleable()
    {
        Assert.True(new IrkallaKafkaOptions().BuildProducerConfig().EnableIdempotence);
        Assert.False(new IrkallaKafkaOptions { EnableIdempotence = false }.BuildProducerConfig().EnableIdempotence);
    }
}
