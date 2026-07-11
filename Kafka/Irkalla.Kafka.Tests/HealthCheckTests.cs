using Irkalla.Kafka.HealthChecks;
using Irkalla.Kafka.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Irkalla.Kafka.Tests;

public class HealthCheckTests
{
    private static Task<HealthCheckResult> Check(ConsumerHealthState state)
        => new IrkallaKafkaHealthCheck(state).CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Healthy_When_All_Running()
    {
        var state = new ConsumerHealthState();
        state.Report("1", "topic-a", ConsumerStatus.Running);
        state.Report("2", "topic-b", ConsumerStatus.Running);

        Assert.Equal(HealthStatus.Healthy, (await Check(state)).Status);
    }

    [Fact]
    public async Task Unhealthy_When_Any_Faulted()
    {
        var state = new ConsumerHealthState();
        state.Report("1", "topic-a", ConsumerStatus.Running);
        state.Report("2", "topic-b", ConsumerStatus.Faulted, "boom");

        var result = await Check(state);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("boom", result.Description);
    }

    [Fact]
    public async Task Degraded_While_Starting()
    {
        var state = new ConsumerHealthState();
        state.Report("1", "topic-a", ConsumerStatus.Starting);

        Assert.Equal(HealthStatus.Degraded, (await Check(state)).Status);
    }

    [Fact]
    public async Task Healthy_When_No_Consumers()
    {
        Assert.Equal(HealthStatus.Healthy, (await Check(new ConsumerHealthState())).Status);
    }
}
