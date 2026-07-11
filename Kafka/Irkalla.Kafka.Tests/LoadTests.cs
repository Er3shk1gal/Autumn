using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Irkalla.Kafka.Attributes;
using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Extensions;
using Irkalla.Kafka.Hosting;
using Irkalla.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.Kafka;
using Xunit;
using Xunit.Abstractions;

namespace Irkalla.Kafka.Tests;

// Fast counting handler — measures FRAMEWORK overhead, not handler work.
[KafkaService("load-topic", "load-svc", HandlerType = MessageHandlerType.JSON, RequestPartitions = 4)]
public class LoadHandlerService
{
    public static long Processed;

    [KafkaMethod("work")]
    public void Work(string payload) => Interlocked.Increment(ref Processed);
}

// Sustained-load + memory-leak test. Runs N messages through a live consumer over R cycles,
// forcing GC between cycles and asserting the managed heap does NOT grow monotonically —
// catches per-message scope leaks, header accumulation, escaped allocations.
[Collection("load")]
public class LoadTests(ITestOutputHelper output) : IAsyncLifetime
{
    private KafkaContainer _kafka = null!;
    private string _bootstrap = null!;

    private const int MessagesPerCycle = 10_000;
    private const int Cycles = 3;
    private const int Partitions = 4;

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.4.0").Build();
        await _kafka.StartAsync();
        _bootstrap = _kafka.GetBootstrapAddress();
    }

    public async Task DisposeAsync() => await _kafka.DisposeAsync();

    private MessageHandlerConfig BuildConfig()
    {
        var method = typeof(LoadHandlerService).GetMethod(nameof(LoadHandlerService.Work))!;
        return new MessageHandlerConfig
        {
            RequestTopicConfig = new TopicConfig { TopicName = "load-topic", PartitionsCount = Partitions, ReplicationFactor = 1 },
            HandlerType = MessageHandlerType.JSON,
            KafkaMethodExecutionConfigs = new Dictionary<string, KafkaMethodExecutionConfig>
            {
                ["work"] = new()
                {
                    KafkaMethodName = "work",
                    ServiceMethodPair = new ServiceMethodPair
                    {
                        Service = typeof(LoadHandlerService),
                        Method = method,
                        Parameters = method.GetParameters(),
                    },
                },
            },
        };
    }

    private void ProduceBatch(int count)
    {
        var config = new ProducerConfig { BootstrapServers = _bootstrap, LingerMs = 20, BatchSize = 128 * 1024 };
        using var producer = new ProducerBuilder<string, byte[]>(config).Build();
        var methodHeader = Encoding.UTF8.GetBytes("work");
        for (int i = 0; i < count; i++)
        {
            producer.Produce("load-topic", new Message<string, byte[]>
            {
                Key = (i % Partitions).ToString(),
                Value = JsonSerializer.SerializeToUtf8Bytes($"msg-{i}"),
                Headers = [new Header("method", methodHeader)],
            });
        }
        producer.Flush(TimeSpan.FromSeconds(30));
    }

    private static long HeapAfterFullGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private async Task DrainTo(long target, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (Interlocked.Read(ref LoadHandlerService.Processed) < target)
        {
            if (sw.Elapsed > timeout)
                throw new TimeoutException($"Drained only {Interlocked.Read(ref LoadHandlerService.Processed)}/{target} in {timeout}");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task SustainedLoad_NoHeapLeak_And_StableThreads()
    {
        LoadHandlerService.Processed = 0;

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureServices(services =>
            {
                services.AddSingleton<LoadHandlerService>();
                services.AddIrkallaKafka(o =>
                {
                    o.BootstrapServers = _bootstrap;
                    o.GroupId = "load-group";
                    o.ServiceAssembly = typeof(IrkallaKafkaOptions).Assembly; // no [KafkaService] there → no auto-scan
                    o.AutoCreateTopics = true;
                    o.AutoOffsetReset = AutoOffsetReset.Earliest;
                });
                var config = BuildConfig();
                services.AddSingleton<IHostedService>(sp =>
                    new KafkaConsumerHostedService(config, sp, sp.GetRequiredService<ILogger<KafkaConsumerHostedService>>()));
            })
            .Build();

        await host.StartAsync();
        // Warm up: one small batch so JIT / consumer / connections settle before baseline.
        ProduceBatch(500);
        await DrainTo(500, TimeSpan.FromSeconds(30));

        var baselineHeap = HeapAfterFullGc();
        var baselineThreads = ThreadPool.ThreadCount;
        var samples = new List<long>();
        long processedTarget = 500;
        var totalSw = Stopwatch.StartNew();

        for (int c = 1; c <= Cycles; c++)
        {
            ProduceBatch(MessagesPerCycle);
            processedTarget += MessagesPerCycle;
            await DrainTo(processedTarget, TimeSpan.FromSeconds(120));

            var heap = HeapAfterFullGc();
            samples.Add(heap);
            output.WriteLine($"cycle {c}: heap={heap / 1024 / 1024.0:F1} MB, threads={ThreadPool.ThreadCount}, processed={Interlocked.Read(ref LoadHandlerService.Processed)}");
        }

        totalSw.Stop();
        var totalMsgs = (double)MessagesPerCycle * Cycles;
        var throughput = totalMsgs / totalSw.Elapsed.TotalSeconds;

        var finalHeap = samples[^1];
        var growthMb = (finalHeap - baselineHeap) / 1024 / 1024.0;
        var cycle1to3Mb = (samples[^1] - samples[0]) / 1024 / 1024.0;

        output.WriteLine($"baseline={baselineHeap / 1024 / 1024.0:F1} MB, final={finalHeap / 1024 / 1024.0:F1} MB, growth={growthMb:F1} MB");
        output.WriteLine($"cycle1→3 heap delta={cycle1to3Mb:F1} MB (steady-state leak signal)");
        output.WriteLine($"throughput≈{throughput:F0} msg/s over {totalMsgs:F0} msgs in {totalSw.Elapsed.TotalSeconds:F1}s");
        output.WriteLine($"threads baseline={baselineThreads}, final={ThreadPool.ThreadCount}");

        await host.StopAsync();

        // Steady-state: heap after cycle 3 must not have grown materially over cycle 1.
        // A real per-message leak at 20k extra messages would blow far past this.
        Assert.True(cycle1to3Mb < 20, $"Steady-state heap grew {cycle1to3Mb:F1} MB from cycle 1→3 — possible leak");
        // Thread count must be stable (one consumer thread, not growing per cycle).
        Assert.True(ThreadPool.ThreadCount <= baselineThreads + 4, $"Thread count grew from {baselineThreads} to {ThreadPool.ThreadCount}");
        // Sanity: everything was actually processed.
        Assert.Equal(processedTarget, Interlocked.Read(ref LoadHandlerService.Processed));
    }
}
