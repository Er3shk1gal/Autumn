using System.Linq;
using Irkalla.Kafka.Configuration;
using Confluent.Kafka;
using Xunit;

namespace Irkalla.Kafka.Tests;

// Proves Kafka configuration flexibility is preserved: typed Security (SSL/SASL) applies to every
// client, a raw key/value layer covers anything not surfaced, and callbacks override everything.
// Precedence: defaults < typed props < RawConfig < Configure* callback.
public class SecurityConfigTests
{
    private static string? Raw(ClientConfig c, string key)
        => c.FirstOrDefault(kv => kv.Key == key).Value;

    [Fact]
    public void Ssl_Sasl_Settings_Flow_To_Consumer()
    {
        var o = new IrkallaKafkaOptions
        {
            BootstrapServers = "broker:9093",
            GroupId = "g",
            Security = new KafkaSecurityOptions
            {
                SecurityProtocol = SecurityProtocol.SaslSsl,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = "user",
                SaslPassword = "pw",
                SslCaLocation = "/certs/ca.pem",
                EnableSslCertificateVerification = false,
            },
        };

        var c = o.BuildConsumerConfig();

        Assert.Equal(SecurityProtocol.SaslSsl, c.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, c.SaslMechanism);
        Assert.Equal("user", c.SaslUsername);
        Assert.Equal("/certs/ca.pem", c.SslCaLocation);
        Assert.False(c.EnableSslCertificateVerification);
    }

    [Fact]
    public void Security_Applies_To_Producer_And_Admin_Too()
    {
        var o = new IrkallaKafkaOptions
        {
            GroupId = "g",
            Security = new KafkaSecurityOptions { SecurityProtocol = SecurityProtocol.Ssl, SslCaLocation = "/ca" },
        };

        Assert.Equal(SecurityProtocol.Ssl, o.BuildProducerConfig().SecurityProtocol);
        Assert.Equal(SecurityProtocol.Ssl, o.BuildAdminClientConfig().SecurityProtocol);
        Assert.Equal("/ca", o.BuildAdminClientConfig().SslCaLocation);
    }

    [Fact]
    public void RawConfig_Covers_Unsurfaced_Keys()
    {
        var o = new IrkallaKafkaOptions
        {
            GroupId = "g",
            RawConfig = { ["fetch.max.bytes"] = "1048576", ["max.poll.interval.ms"] = "600000" },
        };

        var c = o.BuildConsumerConfig();
        Assert.Equal("1048576", Raw(c, "fetch.max.bytes"));
        Assert.Equal("600000", Raw(c, "max.poll.interval.ms"));
    }

    [Fact]
    public void Precedence_RawConfig_Overrides_Typed_And_Callback_Overrides_Raw()
    {
        var o = new IrkallaKafkaOptions
        {
            GroupId = "g",
            Security = new KafkaSecurityOptions { SaslUsername = "typed" },
            RawConfig = { ["sasl.username"] = "raw", ["client.id"] = "raw-id" },
            ConfigureConsumer = c => c.ClientId = "callback-id",
        };

        var cfg = o.BuildConsumerConfig();
        Assert.Equal("raw", cfg.SaslUsername);      // raw (layer 3) overrides typed (layer 2)
        Assert.Equal("callback-id", cfg.ClientId);  // callback (layer 4) overrides raw (layer 3)
    }

    [Fact]
    public void AdminClient_Callback_Is_Applied()
    {
        var o = new IrkallaKafkaOptions
        {
            GroupId = "g",
            ConfigureAdminClient = a => a.ClientId = "admin-cb",
        };
        Assert.Equal("admin-cb", o.BuildAdminClientConfig().ClientId);
    }

    [Fact]
    public void SchemaRegistry_Callback_Sets_BasicAuth()
    {
        var o = new IrkallaKafkaOptions
        {
            GroupId = "g",
            SchemaRegistryUrl = "http://sr:8081",
            ConfigureSchemaRegistry = sr => sr.BasicAuthUserInfo = "key:secret",
        };
        var cfg = o.BuildSchemaRegistryConfig();
        Assert.Equal("http://sr:8081", cfg.Url);
        Assert.Equal("key:secret", cfg.BasicAuthUserInfo);
    }

    [Fact]
    public void RawConfig_Cannot_Bypass_ManualCommit_Invariant()
    {
        // The one deliberate restriction: enabling auto-commit breaks at-least-once, so even the
        // raw escape hatch is guarded.
        var o = new IrkallaKafkaOptions
        {
            GroupId = "g",
            RawConfig = { ["enable.auto.commit"] = "true" },
        };
        Assert.Throws<Irkalla.Kafka.Exceptions.KafkaConfigurationException>(() => o.BuildConsumerConfig());
    }
}
