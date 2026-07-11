using System.Reflection;
using System.Text;
using System.Runtime.ExceptionServices;
using Autumn.Kafka.Attributes;
using Autumn.Kafka.Configuration;
using Autumn.Kafka.Exceptions;
using Autumn.Kafka.MessageHandlers;
using Autumn.Kafka.Utils;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Autumn.Kafka.Tests;

#region Test Classes for Factory Scanning
[KafkaService("topic1", "valid-service", HandlerType = MessageHandlerType.JSON, ResponseTopic = "response-topic")]
public class ValidService
{
    [KafkaMethod("method1")]
    public void Method1(string payload) { }

    [KafkaMethod("method2", RequiresResponse = true)]
    public string Method2(string payload) => "response";
}

[KafkaService("topic1", "response-service", HandlerType = MessageHandlerType.JSON, ResponseTopic = "response1")]
public class ValidServiceWithResponse
{
    [KafkaMethod("method3", RequiresResponse = true)]
    public string Method3(string payload) => "response";
}

[KafkaService("topic-no-method", "no-method-service")]
public class ServiceWithoutMethods
{
    public void SomeMethod() { }
}

[KafkaService("topic-dup", "dup-service1", HandlerType = MessageHandlerType.JSON)]
public class ServiceWithDuplicateMethod1
{
    [KafkaMethod("dup-method")]
    public void MethodA(string payload) { }
}

[KafkaService("topic-dup", "dup-service2", HandlerType = MessageHandlerType.JSON)]
public class ServiceWithDuplicateMethod2
{
    [KafkaMethod("dup-method")]
    public void MethodB(string payload) { }
}

[KafkaService("topic-conflicting-type", "conflicting-service1", HandlerType = MessageHandlerType.AVRO)]
public class ConflictingService1
{
    [KafkaMethod("methodA")]
    public void MethodA(string payload) { }
}

[KafkaService("topic-conflicting-type", "conflicting-service2", HandlerType = MessageHandlerType.JSON)]
public class ConflictingService2
{
    [KafkaMethod("methodB")]
    public void MethodB(string payload) { }
}

[KafkaService("topic-requires-resp-no-topic", "requires-resp-no-topic-service")]
public class ServiceRequiresResponseNoTopic
{
    [KafkaMethod("methodResp", RequiresResponse = true)]
    public string MethodA(string payload) => "res";
}

[KafkaService("topic-multiple-payloads", "multiple-payloads-service")]
public class ServiceMultiplePayloads
{
    [KafkaMethod("methodA")]
    public void MethodA(string payload1, string payload2) { }
}

[KafkaService("topic-protobuf-invalid", "protobuf-invalid-service", HandlerType = MessageHandlerType.PROTOBUF)]
public class ServiceProtobufInvalid
{
    [KafkaMethod("methodA")]
    public void MethodA(string payload1) { }
}

public class TestProtobufMessage : Google.Protobuf.IMessage<TestProtobufMessage>
{
    public void MergeFrom(TestProtobufMessage message) { }
    public void MergeFrom(Google.Protobuf.CodedInputStream input) { }
    public void WriteTo(Google.Protobuf.CodedOutputStream output) { }
    public int CalculateSize() => 0;
    public Google.Protobuf.Reflection.MessageDescriptor Descriptor => null!;
    public bool Equals(TestProtobufMessage? other) => true;
    public TestProtobufMessage Clone() => new TestProtobufMessage();
}

[KafkaService("topic-protobuf-valid", "protobuf-valid-service", HandlerType = MessageHandlerType.PROTOBUF)]
public class ServiceProtobufValid
{
    [KafkaMethod("methodA")]
    public void MethodA(TestProtobufMessage payload1) { }
}
#endregion

public class MessageHandlerFactoryTests
{
    private class DummyAssembly(params Type[] types) : Assembly
    {
        public override Type[] GetTypes() => types;
    }

    [Fact]
    public void BuildHandlerConfigs_ValidService_ReturnsCorrectConfig()
    {
        var assembly = new DummyAssembly(typeof(ValidService));
        var configs = MessageHandlerFactory.BuildHandlerConfigs(assembly).ToList();

        Assert.Single(configs);
        var config = configs.First();
        Assert.Equal("topic1", config.RequestTopicConfig.TopicName);
        Assert.Equal(2, config.KafkaMethodExecutionConfigs.Count);
        Assert.True(config.KafkaMethodExecutionConfigs.ContainsKey("method1"));
        Assert.True(config.KafkaMethodExecutionConfigs.ContainsKey("method2"));
    }

    [Fact]
    public void BuildHandlerConfigs_NoKafkaMethod_ThrowsConfigurationException()
    {
        var assembly = new DummyAssembly(typeof(ServiceWithoutMethods));
        Assert.Throws<KafkaConfigurationException>(() => MessageHandlerFactory.BuildHandlerConfigs(assembly));
    }

    [Fact]
    public void BuildHandlerConfigs_DuplicateMethodName_ThrowsConfigurationException()
    {
        var assembly = new DummyAssembly(typeof(ServiceWithDuplicateMethod1), typeof(ServiceWithDuplicateMethod2));
        Assert.Throws<KafkaConfigurationException>(() => MessageHandlerFactory.BuildHandlerConfigs(assembly));
    }

    [Fact]
    public void BuildHandlerConfigs_ConflictingHandlerType_ThrowsConfigurationException()
    {
        var assembly = new DummyAssembly(typeof(ConflictingService1), typeof(ConflictingService2));
        Assert.Throws<KafkaConfigurationException>(() => MessageHandlerFactory.BuildHandlerConfigs(assembly));
    }

    [Fact]
    public void BuildHandlerConfigs_RequiresResponseNoResponseTopic_ThrowsConfigurationException()
    {
        var assembly = new DummyAssembly(typeof(ServiceRequiresResponseNoTopic));
        Assert.Throws<KafkaConfigurationException>(() => MessageHandlerFactory.BuildHandlerConfigs(assembly));
    }

    [Fact]
    public void BuildHandlerConfigs_MultiplePayloadParameters_ThrowsConfigurationException()
    {
        var assembly = new DummyAssembly(typeof(ServiceMultiplePayloads));
        Assert.Throws<KafkaConfigurationException>(() => MessageHandlerFactory.BuildHandlerConfigs(assembly));
    }

    [Fact]
    public void BuildHandlerConfigs_ProtobufNonIMessage_ThrowsConfigurationException()
    {
        var assembly = new DummyAssembly(typeof(ServiceProtobufInvalid));
        Assert.Throws<KafkaConfigurationException>(() => MessageHandlerFactory.BuildHandlerConfigs(assembly));
    }

    [Fact]
    public void BuildHandlerConfigs_ProtobufValidIMessage_Succeeds()
    {
        var assembly = new DummyAssembly(typeof(ServiceProtobufValid));
        var configs = MessageHandlerFactory.BuildHandlerConfigs(assembly).ToList();
        Assert.Single(configs);
    }
}

public interface ISingleInterface { }
public class SingleInterfaceService : ISingleInterface { }

public interface IMultiple1 { }
public interface IMultiple2 { }
public class MultipleInterfaceService : IMultiple1, IMultiple2 { }

public class ServiceResolverTests
{
    [Fact]
    public async Task ResolveService_SingleInterface_ResolvesSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddTransient<ISingleInterface, SingleInterfaceService>();
        var provider = services.BuildServiceProvider();

        var instance = await ServiceResolver.InvokeMethodAsync(provider, typeof(SingleInterfaceService).GetMethod("ToString")!, typeof(SingleInterfaceService), null);
        Assert.NotNull(instance);
    }

    [Fact]
    public async Task ResolveService_MultipleInterfaces_ThrowsAmbiguousResolutionException()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMultiple1, MultipleInterfaceService>();
        services.AddTransient<IMultiple2, MultipleInterfaceService>();
        var provider = services.BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<KafkaServiceResolutionException>(() => 
            ServiceResolver.InvokeMethodAsync(provider, typeof(MultipleInterfaceService).GetMethod("ToString")!, typeof(MultipleInterfaceService), null)
        );

        Assert.Contains("Ambiguous resolution", ex.Message);
    }
}

#region Mock BaseMessageHandler
public class TestMessageHandler : BaseMessageHandler
{
    public Func<byte[], Type, Task<object?>> DeserializeMock { get; set; } = (b, t) => Task.FromResult<object?>(null);
    public Func<object, Type, Task<byte[]>> SerializeMock { get; set; } = (o, t) => Task.FromResult<byte[]>([]);

    public TestMessageHandler(
        KafkaProducer producer,
        IConsumer<string, byte[]> consumer,
        MessageHandlerConfig config,
        ILogger logger,
        IServiceProvider sp,
        AutumnKafkaOptions options)
        : base(producer, consumer, config, logger, sp, options)
    {
    }

    protected override Task<object?> DeserializeAsync(byte[] bytes, Type targetType, SerializationContext context)
        => DeserializeMock(bytes, targetType);

    protected override Task<byte[]> SerializeAsync(object obj, Type targetType, SerializationContext context)
        => SerializeMock(obj, targetType);
}

public class DummyBusinessService
{
    public int CallCount { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    public void Handle(string payload)
    {
        CallCount++;
        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }
    }
}
#endregion

public class MessageHandlerRetryTests
{
    private KafkaProducer CreateProducer(ILoggerFactory loggerFactory)
    {
        var mockIProducer = new Mock<IProducer<string, byte[]>>();
        var mockAdmin = new Mock<IAdminClient>();
        var topicManager = new KafkaTopicManager(mockAdmin.Object, loggerFactory.CreateLogger<KafkaTopicManager>());
        return new KafkaProducer(mockIProducer.Object, loggerFactory.CreateLogger<KafkaProducer>(), topicManager);
    }

    [Fact]
    public async Task Consume_DeterministicException_NoRetry()
    {
        // Setup
        var options = new AutumnKafkaOptions
        {
            MaxRetries = 3,
            ErrorPolicy = ErrorPolicy.Skip,
            RetryDelay = TimeSpan.FromMilliseconds(5)
        };

        var service = new DummyBusinessService
        {
            ExceptionToThrow = new KafkaConsumerException("Deterministic error")
        };

        var services = new ServiceCollection();
        services.AddSingleton(service);
        var serviceProvider = services.BuildServiceProvider();

        var mockConsumer = new Mock<IConsumer<string, byte[]>>();
        var loggerFactory = new LoggerFactory();
        var producer = CreateProducer(loggerFactory);
        var logger = loggerFactory.CreateLogger("Test");

        // Construct mock Message
        var headers = new Headers();
        headers.Add("method", Encoding.UTF8.GetBytes("handle"));

        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Message = new Message<string, byte[]>
            {
                Key = "key",
                Value = Encoding.UTF8.GetBytes("payload"),
                Headers = headers
            },
            Partition = new Partition(0),
            Offset = new Offset(0)
        };

        // Let the consumer return the message once, then throw OperationCanceledException to stop loop
        int consumeCalls = 0;
        mockConsumer.Setup(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                consumeCalls++;
                if (consumeCalls == 1) return consumeResult;
                throw new OperationCanceledException();
            });

        // Config
        var config = new MessageHandlerConfig
        {
            RequestTopicConfig = new TopicConfig { TopicName = "topic" },
            HandlerType = MessageHandlerType.JSON,
            KafkaMethodExecutionConfigs = new Dictionary<string, KafkaMethodExecutionConfig>
            {
                {
                    "handle", new KafkaMethodExecutionConfig
                    {
                        KafkaMethodName = "handle",
                        ServiceMethodPair = new ServiceMethodPair
                        {
                            Service = typeof(DummyBusinessService),
                            Method = typeof(DummyBusinessService).GetMethod("Handle")!,
                            Parameters = typeof(DummyBusinessService).GetMethod("Handle")!.GetParameters()
                        }
                    }
                }
            }
        };

        var handler = new TestMessageHandler(producer, mockConsumer.Object, config, logger, serviceProvider, options);
        handler.DeserializeMock = (bytes, type) => Task.FromResult<object?>(Encoding.UTF8.GetString(bytes));

        // Act
        await handler.Consume(CancellationToken.None);

        // Assert: 1 call to service, no retries
        Assert.Equal(1, service.CallCount);
        mockConsumer.Verify(c => c.Commit(consumeResult), Times.Once);
    }

    [Fact]
    public async Task Consume_TransientException_RetriesAndBackoff()
    {
        // Setup
        var options = new AutumnKafkaOptions
        {
            MaxRetries = 2,
            ErrorPolicy = ErrorPolicy.Skip,
            RetryDelay = TimeSpan.FromMilliseconds(5)
        };

        var service = new DummyBusinessService
        {
            ExceptionToThrow = new InvalidOperationException("Transient error")
        };

        var services = new ServiceCollection();
        services.AddSingleton(service);
        var serviceProvider = services.BuildServiceProvider();

        var mockConsumer = new Mock<IConsumer<string, byte[]>>();
        var loggerFactory = new LoggerFactory();
        var producer = CreateProducer(loggerFactory);
        var logger = loggerFactory.CreateLogger("Test");

        // Construct mock Message
        var headers = new Headers();
        headers.Add("method", Encoding.UTF8.GetBytes("handle"));

        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Message = new Message<string, byte[]>
            {
                Key = "key",
                Value = Encoding.UTF8.GetBytes("payload"),
                Headers = headers
            },
            Partition = new Partition(0),
            Offset = new Offset(0)
        };

        int consumeCalls = 0;
        mockConsumer.Setup(c => c.Consume(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                consumeCalls++;
                if (consumeCalls == 1) return consumeResult;
                throw new OperationCanceledException();
            });

        // Config
        var config = new MessageHandlerConfig
        {
            RequestTopicConfig = new TopicConfig { TopicName = "topic" },
            HandlerType = MessageHandlerType.JSON,
            KafkaMethodExecutionConfigs = new Dictionary<string, KafkaMethodExecutionConfig>
            {
                {
                    "handle", new KafkaMethodExecutionConfig
                    {
                        KafkaMethodName = "handle",
                        ServiceMethodPair = new ServiceMethodPair
                        {
                            Service = typeof(DummyBusinessService),
                            Method = typeof(DummyBusinessService).GetMethod("Handle")!,
                            Parameters = typeof(DummyBusinessService).GetMethod("Handle")!.GetParameters()
                        }
                    }
                }
            }
        };

        var handler = new TestMessageHandler(producer, mockConsumer.Object, config, logger, serviceProvider, options);
        handler.DeserializeMock = (bytes, type) => Task.FromResult<object?>(Encoding.UTF8.GetString(bytes));

        // Act
        await handler.Consume(CancellationToken.None);

        // Assert: 1 initial attempt + 2 retries = 3 total attempts
        Assert.Equal(3, service.CallCount);
        mockConsumer.Verify(c => c.Commit(consumeResult), Times.Once);
    }
}
