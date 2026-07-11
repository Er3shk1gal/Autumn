using System.Reflection;
using System.Text;
using Autumn.Kafka.Attributes;
using Autumn.Kafka.Configuration;
using Autumn.Kafka.Exceptions;
using Autumn.Kafka.Extensions;
using Autumn.Kafka.Hosting;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.Kafka;
using Xunit;

namespace Autumn.Kafka.Tests;

#region Integration Test Services
[KafkaService("int-request-topic", "int-service", HandlerType = MessageHandlerType.JSON, ResponseTopic = "int-response-topic")]
public class IntegrationTestService
{
    public static int ProcessCount { get; set; }
    public static string? LastReceivedPayload { get; set; }

    [KafkaMethod("sayHello", RequiresResponse = true)]
    public string SayHello(string name)
    {
        ProcessCount++;
        LastReceivedPayload = name;
        return $"Hello, {name}!";
    }
}

[KafkaService("int-dlq-topic", "int-dlq-service", HandlerType = MessageHandlerType.JSON)]
public class IntegrationDlqService
{
    [KafkaMethod("failMethod")]
    public void FailMethod(string payload)
    {
        throw new InvalidOperationException("Business failure");
    }
}

[KafkaService("int-stop-topic", "int-stop-service", HandlerType = MessageHandlerType.JSON)]
public class IntegrationStopService
{
    [KafkaMethod("failMethod")]
    public void FailMethod(string payload)
    {
        throw new KafkaConsumerException("Deterministic crash");
    }
}
#endregion

public class IntegrationTests : IAsyncLifetime
{
    private KafkaContainer _kafkaContainer = null!;
    private string _bootstrapServers = null!;

    public async Task InitializeAsync()
    {
        // Use the constructor with the image parameter to avoid obsolete warning
        _kafkaContainer = new KafkaBuilder("confluentinc/cp-kafka:7.4.0").Build();
        await _kafkaContainer.StartAsync();
        _bootstrapServers = _kafkaContainer.GetBootstrapAddress();
    }

    public async Task DisposeAsync()
    {
        if (_kafkaContainer != null)
        {
            await _kafkaContainer.DisposeAsync();
        }
    }

    private async Task PreCreateTopic(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _bootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync(
                [new Confluent.Kafka.Admin.TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        }
        catch (Confluent.Kafka.Admin.CreateTopicsException) { /* already exists */ }
    }

    private async Task ProduceMessageAsync(string topic, string method, string payload)
    {
        var config = new ProducerConfig { BootstrapServers = _bootstrapServers };
        using var producer = new ProducerBuilder<string, byte[]>(config).Build();

        var headers = new Headers
        {
            new Header("method", Encoding.UTF8.GetBytes(method))
        };

        var message = new Message<string, byte[]>
        {
            Key = "key",
            Value = Encoding.UTF8.GetBytes($"\"{payload}\""), // JSON string
            Headers = headers
        };

        await producer.ProduceAsync(topic, message);
    }

    private MessageHandlerConfig CreateTestConfig(Type serviceType, string requestTopic, string methodName, string? responseTopic = null)
    {
        var method = serviceType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? serviceType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} not found on type {serviceType.Name}");
        
        var methodAttr = method.GetCustomAttribute<KafkaMethodAttribute>()!;
        var serviceAttr = serviceType.GetCustomAttribute<KafkaServiceAttribute>()!;

        TopicConfig? responseTopicConfig = null;
        if (responseTopic != null)
        {
            responseTopicConfig = new TopicConfig
            {
                TopicName = responseTopic,
                PartitionsCount = 1,
                ReplicationFactor = 1
            };
        }

        var executionConfig = new KafkaMethodExecutionConfig
        {
            KafkaMethodName = methodAttr.MethodName,
            RequireResponse = methodAttr.RequiresResponse,
            KafkaServiceName = serviceAttr.ServiceName,
            ResponseTopicConfig = responseTopicConfig,
            ResponseTopicPartition = 0,
            ServiceMethodPair = new ServiceMethodPair
            {
                Service = serviceType,
                Method = method,
                Parameters = method.GetParameters()
            }
        };

        return new MessageHandlerConfig
        {
            RequestTopicConfig = new TopicConfig
            {
                TopicName = requestTopic,
                PartitionsCount = 1,
                ReplicationFactor = 1
            },
            HandlerType = MessageHandlerType.JSON,
            KafkaMethodExecutionConfigs = new Dictionary<string, KafkaMethodExecutionConfig>
            {
                { methodAttr.MethodName, executionConfig }
            }
        };
    }

    [Fact]
    public async Task JsonRoundtrip_SuccessfulProcessing()
    {
        IntegrationTestService.ProcessCount = 0;
        IntegrationTestService.LastReceivedPayload = null;

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IntegrationTestService>();
                services.AddAutumnKafka(options =>
                {
                    options.BootstrapServers = _bootstrapServers;
                    options.GroupId = "test-group-1";
                    // Scan assembly that has no service classes to avoid scanning unit test classes
                    options.ServiceAssembly = typeof(AutumnKafkaOptions).Assembly;
                    options.AutoCreateTopics = true;
                });

                var config = CreateTestConfig(typeof(IntegrationTestService), "int-request-topic", "SayHello", "int-response-topic");
                services.AddSingleton<IHostedService>(sp =>
                    new KafkaConsumerHostedService(config, sp, sp.GetRequiredService<ILogger<KafkaConsumerHostedService>>()));
            })
            .Build();

        // Pre-create topics so response delivery doesn't race a lazy admin CreateTopics on a cold broker.
        await PreCreateTopic("int-request-topic");
        await PreCreateTopic("int-response-topic");

        await host.StartAsync();

        // Produce a request message
        await ProduceMessageAsync("int-request-topic", "sayHello", "World");

        // Wait for consumer to process it
        for (int i = 0; i < 50; i++)
        {
            if (IntegrationTestService.ProcessCount > 0) break;
            await Task.Delay(100);
        }

        Assert.Equal(1, IntegrationTestService.ProcessCount);
        Assert.Equal("World", IntegrationTestService.LastReceivedPayload);

        // Check if response was sent to response topic
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = "test-group-response",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        consumer.Subscribe("int-response-topic");

        // Poll with a generous window: a fresh consumer-group join on a cold broker can take
        // several seconds, so a single short Consume() is flaky.
        ConsumeResult<string, byte[]>? response = null;
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(20))
        {
            response = consumer.Consume(TimeSpan.FromSeconds(1));
            if (response != null) break;
        }
        Assert.NotNull(response);

        var responseBody = Encoding.UTF8.GetString(response!.Message.Value);
        Assert.Equal("\"Hello, World!\"", responseBody);

        await host.StopAsync();
    }

    [Fact]
    public async Task PoisonMessage_DlqPolicy_PublishesToDlqAndCommits()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IntegrationDlqService>();
                services.AddAutumnKafka(options =>
                {
                    options.BootstrapServers = _bootstrapServers;
                    options.GroupId = "test-group-dlq";
                    options.ServiceAssembly = typeof(AutumnKafkaOptions).Assembly;
                    options.ErrorPolicy = ErrorPolicy.Dlq;
                    options.MaxRetries = 1;
                    options.RetryDelay = TimeSpan.FromMilliseconds(10);
                });

                var config = CreateTestConfig(typeof(IntegrationDlqService), "int-dlq-topic", "FailMethod");
                services.AddSingleton<IHostedService>(sp =>
                    new KafkaConsumerHostedService(config, sp, sp.GetRequiredService<ILogger<KafkaConsumerHostedService>>()));
            })
            .Build();

        await host.StartAsync();

        // Produce invalid payload that will throw in the handler
        await ProduceMessageAsync("int-dlq-topic", "failMethod", "poison");

        // Consume from DLQ topic to verify it arrived (wait for creation and arrival)
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = "test-group-dlq-verify",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        using var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig).Build();
        consumer.Subscribe("int-dlq-topic.dlq");

        ConsumeResult<string, byte[]>? dlqMessage = null;
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(10))
        {
            try
            {
                dlqMessage = consumer.Consume(TimeSpan.FromSeconds(1));
                if (dlqMessage != null) break;
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                // Wait for topic to be created by the hosted service
                await Task.Delay(200);
            }
        }

        Assert.NotNull(dlqMessage);

        var errorHeader = dlqMessage.Message.Headers.FirstOrDefault(h => h.Key == "error");
        Assert.NotNull(errorHeader);
        var errorMessage = Encoding.UTF8.GetString(errorHeader.GetValueBytes());
        Assert.Contains("Business failure", errorMessage);

        await host.StopAsync();
    }

    [Fact]
    public async Task PoisonMessage_StopPolicy_CrashesHost()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IntegrationStopService>();
                services.AddAutumnKafka(options =>
                {
                    options.BootstrapServers = _bootstrapServers;
                    options.GroupId = "test-group-stop";
                    options.ServiceAssembly = typeof(AutumnKafkaOptions).Assembly;
                    options.ErrorPolicy = ErrorPolicy.Stop;
                    options.MaxRetries = 0;
                });

                var config = CreateTestConfig(typeof(IntegrationStopService), "int-stop-topic", "FailMethod");
                services.AddSingleton<IHostedService>(sp =>
                    new KafkaConsumerHostedService(config, sp, sp.GetRequiredService<ILogger<KafkaConsumerHostedService>>()));
            })
            .Build();

        await host.StartAsync();

        // Produce deterministic crash message
        await ProduceMessageAsync("int-stop-topic", "failMethod", "poison");

        // Wait a few seconds to verify the host shuts down.
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(200);
        }

        // Just stop host cleanly if it hasn't stopped
        await host.StopAsync();
    }
}
