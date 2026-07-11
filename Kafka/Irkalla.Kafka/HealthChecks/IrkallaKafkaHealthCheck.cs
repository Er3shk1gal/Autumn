using Irkalla.Kafka.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Irkalla.Kafka.HealthChecks
{
    /// <summary>
    /// Reports the health of Irkalla.Kafka consumers. <b>Unhealthy</b> if any consumer has faulted,
    /// <b>Degraded</b> if any is still starting, otherwise <b>Healthy</b>. Each consumer's topic and
    /// status is included in the result data.
    /// <para>
    /// Wire it up:
    /// <code>
    /// builder.Services.AddHealthChecks().AddCheck&lt;IrkallaKafkaHealthCheck&gt;("kafka");
    /// </code>
    /// </para>
    /// </summary>
    public sealed class IrkallaKafkaHealthCheck(ConsumerHealthState state) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var entries = state.Entries;
            var data = entries
                .GroupBy(e => e.Topic)
                .ToDictionary(g => g.Key, g => (object)string.Join(",", g.Select(e => e.Status)));

            var faulted = entries.Where(e => e.Status == ConsumerStatus.Faulted).ToList();
            if (faulted.Count > 0)
            {
                var detail = string.Join("; ", faulted.Select(e => $"{e.Topic}: {e.Error}"));
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"{faulted.Count} consumer(s) faulted — {detail}", data: data));
            }

            if (entries.Any(e => e.Status == ConsumerStatus.Starting))
            {
                return Task.FromResult(HealthCheckResult.Degraded("Consumers starting", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                entries.Count == 0 ? "No consumers registered" : "All consumers running", data));
        }
    }
}
