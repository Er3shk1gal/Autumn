using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Extensions;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Irkalla.Kafka.Tests;

public class ConfigurationBindingTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Binds_Options_From_IrkallaKafka_Section()
    {
        var config = Config(new()
        {
            ["IrkallaKafka:BootstrapServers"] = "broker:9093",
            ["IrkallaKafka:GroupId"] = "g1",
            ["IrkallaKafka:ErrorPolicy"] = "Skip",
            ["IrkallaKafka:ConsumerMode"] = "Auto",
            ["IrkallaKafka:MaxRetries"] = "5",
            ["IrkallaKafka:IncludeStackTraceInDlq"] = "true",
            ["IrkallaKafka:Security:SecurityProtocol"] = "SaslSsl",
            ["IrkallaKafka:Security:SaslUsername"] = "user",
            ["IrkallaKafka:RawConfig:fetch.max.bytes"] = "1048576",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIrkallaKafka(config, o => o.ServiceAssembly = typeof(IrkallaKafkaOptions).Assembly);
        var sp = services.BuildServiceProvider();

        var o = sp.GetRequiredService<IrkallaKafkaOptions>();
        Assert.Equal("broker:9093", o.BootstrapServers);
        Assert.Equal("g1", o.GroupId);
        Assert.Equal(ErrorPolicy.Skip, o.ErrorPolicy);
        Assert.Equal(ConsumerMode.Auto, o.ConsumerMode);
        Assert.Equal(5, o.MaxRetries);
        Assert.True(o.IncludeStackTraceInDlq);
        Assert.Equal(SecurityProtocol.SaslSsl, o.Security.SecurityProtocol);
        Assert.Equal("user", o.Security.SaslUsername);
        Assert.Equal("1048576", o.RawConfig["fetch.max.bytes"]);

        // IOptions view over the same snapshot
        Assert.Same(o, sp.GetRequiredService<IOptions<IrkallaKafkaOptions>>().Value);
    }

    [Fact]
    public void Code_Callback_Overrides_Configuration()
    {
        var config = Config(new()
        {
            ["IrkallaKafka:GroupId"] = "from-config",
            ["IrkallaKafka:BootstrapServers"] = "cfg:9092",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIrkallaKafka(config, o =>
        {
            o.GroupId = "from-code";
            o.ServiceAssembly = typeof(IrkallaKafkaOptions).Assembly;
        });
        var sp = services.BuildServiceProvider();

        var o = sp.GetRequiredService<IrkallaKafkaOptions>();
        Assert.Equal("from-code", o.GroupId);         // code wins
        Assert.Equal("cfg:9092", o.BootstrapServers);  // config kept where not overridden
    }
}
