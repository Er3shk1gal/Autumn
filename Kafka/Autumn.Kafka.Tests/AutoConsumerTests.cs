using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Autumn.Kafka.Attributes;
using Autumn.Kafka.Configuration;
using Autumn.Kafka.Extensions;
using Autumn.Kafka.Hosting;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.Kafka;
using Xunit;
using Xunit.Abstractions;

namespace Autumn.Kafka.Tests;

// A handler with real per-message work (blocking). Auto mode parallelizes ACROSS partitions via
// multiple consumers — so its benefit shows when the handler, not the framework, is the bottleneck.
public class SlowHandlerService
{
    public static long Processed;
    public static int WorkMs = 2;
    public static int Current;
    public static int PeakConcurrency;
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> Threads = new();

    public void Work(string payload)
    {
        var cur = Interlocked.Increment(ref Current);
        int peak;
        while (cur > (peak = Volatile.Read(ref PeakConcurrency)))
            Interlocked.CompareExchange(ref PeakConcurrency, cur, peak);
        Threads[Environment.CurrentManagedThreadId] = 0;
        Thread.Sleep(WorkMs);
        Interlocked.Decrement(ref Current);
        Interlocked.Increment(ref Processed);
    }
}

// Verifies ConsumerMode.Auto: K consumers in the same group on a K-partition topic split the
// partitions (Subscribe/group protocol), process every message EXACTLY once (no duplication,
// no loss), and — for a bottlenecked handler — beat a single consumer on throughput.
[Collection("load")]
public class AutoConsumerTests(ITestOutputHelper output) : IAsyncLifetime
{
    private KafkaContainer _kafka = null!;
    private string _bootstrap = null!;

    private const int Partitions = 4;
    private const int Messages = 4_000;

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.4.0").Build();
        await _kafka.StartAsync();
        _bootstrap = _kafka.GetBootstrapAddress();
    }

    public async Task DisposeAsync() => await _kafka.DisposeAsync();

    // Pre-create the topic with the exact partition count. Otherwise the producer's first send
    // auto-creates it on the broker with num.partitions=1, and the whole topic has a single
    // partition — so no amount of consumers can parallelize.
    private async Task CreateTopic(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrap }).Build();
        try
        {
            await admin.CreateTopicsAsync(
            [
                new Confluent.Kafka.Admin.TopicSpecification { Name = topic, NumPartitions = Partitions, ReplicationFactor = 1 }
            ]);
        }
        catch (Confluent.Kafka.Admin.CreateTopicsException) { /* already exists */ }
    }

    private MessageHandlerConfig BuildConfig(string topic)
    {
        var method = typeof(SlowHandlerService).GetMethod(nameof(SlowHandlerService.Work))!;
        return new MessageHandlerConfig
        {
            RequestTopicConfig = new TopicConfig { TopicName = topic, PartitionsCount = Partitions, ReplicationFactor = 1 },
            HandlerType = MessageHandlerType.JSON,
            KafkaMethodExecutionConfigs = new Dictionary<string, KafkaMethodExecutionConfig>
            {
                ["work"] = new()
                {
                    KafkaMethodName = "work",
                    ServiceMethodPair = new ServiceMethodPair
                    {
                        Service = typeof(SlowHandlerService),
                        Method = method,
                        Parameters = method.GetParameters(),
                    },
                },
            },
        };
    }

    private void Produce(string topic, int count, int keyOffset = 0)
    {
        var config = new ProducerConfig { BootstrapServers = _bootstrap, LingerMs = 20, BatchSize = 128 * 1024 };
        using var producer = new ProducerBuilder<string, byte[]>(config).Build();
        var header = Encoding.UTF8.GetBytes("work");
        for (int i = 0; i < count; i++)
        {
            // Explicit round-robin partition → guaranteed even spread across all partitions.
            producer.Produce(new TopicPartition(topic, new Partition(i % Partitions)), new Message<string, byte[]>
            {
                Key = (keyOffset + i).ToString(),
                Value = JsonSerializer.SerializeToUtf8Bytes($"m{i}"),
                Headers = [new Header("method", header)],
            });
        }
        producer.Flush(TimeSpan.FromSeconds(30));
    }

    private IHost BuildHost(string topic, int consumerCount)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Error))
            .ConfigureServices(services =>
            {
                services.AddSingleton<SlowHandlerService>();
                services.AddAutumnKafka(o =>
                {
                    o.BootstrapServers = _bootstrap;
                    o.GroupId = $"auto-group-{topic}";
                    o.ServiceAssembly = typeof(AutumnKafkaOptions).Assembly; // no auto-scan
                    o.AutoCreateTopics = false; // topic is pre-created with the right partition count
                    o.AutoOffsetReset = AutoOffsetReset.Earliest;
                });
                var config = BuildConfig(topic);
                // Register `consumerCount` identical consumers — exactly what ConsumerMode.Auto does
                // for a topic with `consumerCount` partitions.
                for (int i = 0; i < consumerCount; i++)
                {
                    services.AddSingleton<IHostedService>(sp =>
                        new KafkaConsumerHostedService(config, sp, sp.GetRequiredService<ILogger<KafkaConsumerHostedService>>()));
                }
            })
            .Build();
    }

    private async Task DrainTo(long target, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (Interlocked.Read(ref SlowHandlerService.Processed) < target)
        {
            if (sw.Elapsed > timeout)
                throw new TimeoutException($"Only {Interlocked.Read(ref SlowHandlerService.Processed)}/{target} processed");
            await Task.Delay(25);
        }
    }

    private async Task<double> RunAsync(string topic, int consumerCount)
    {
        SlowHandlerService.Processed = 0;
        await CreateTopic(topic);
        using var host = BuildHost(topic, consumerCount);
        await host.StartAsync();

        // Warm up: create topic, join group, settle rebalances BEFORE timing.
        Produce(topic, 200);
        await DrainTo(200, TimeSpan.FromSeconds(60));

        Interlocked.Exchange(ref SlowHandlerService.Processed, 0);
        Interlocked.Exchange(ref SlowHandlerService.PeakConcurrency, 0);
        SlowHandlerService.Threads.Clear();
        var sw = Stopwatch.StartNew();
        Produce(topic, Messages, keyOffset: 1_000_000);
        await DrainTo(Messages, TimeSpan.FromSeconds(120));
        sw.Stop();

        // Settle window: a double-processed partition would overshoot the count.
        await Task.Delay(1500);
        Assert.Equal(Messages, Interlocked.Read(ref SlowHandlerService.Processed)); // exactly-once

        output.WriteLine($"  [{topic}] consumers={consumerCount} peakConcurrency={SlowHandlerService.PeakConcurrency} threads={SlowHandlerService.Threads.Count}");
        await host.StopAsync();
        return Messages / sw.Elapsed.TotalSeconds;
    }

    [Fact]
    public async Task Auto_MultipleConsumers_ExactlyOnce_And_FasterThanSingle()
    {
        var singleTps = await RunAsync("auto-single-topic", consumerCount: 1);
        output.WriteLine($"single (1 consumer): {singleTps:F0} msg/s");

        var autoTps = await RunAsync("auto-multi-topic", consumerCount: Partitions);
        output.WriteLine($"auto ({Partitions} consumers): {autoTps:F0} msg/s");
        output.WriteLine($"speedup: {autoTps / singleTps:F2}x (handler work={SlowHandlerService.WorkMs}ms/msg)");

        // With a bottlenecked handler, K consumers over K partitions must give a real gain.
        // Conservative floor (well under linear Kx) to stay robust on shared CI.
        Assert.True(autoTps > singleTps * 2.0,
            $"Auto ({autoTps:F0}) not meaningfully faster than single ({singleTps:F0})");
    }
}
