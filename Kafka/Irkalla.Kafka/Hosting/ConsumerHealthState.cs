using System.Collections.Concurrent;

namespace Irkalla.Kafka.Hosting
{
    /// <summary>Lifecycle status of a single consumer.</summary>
    public enum ConsumerStatus
    {
        Starting,
        Running,
        Stopped,
        Faulted
    }

    /// <summary>
    /// Thread-safe registry of consumer statuses, updated by each
    /// <see cref="KafkaConsumerHostedService"/> and read by the health check. Registered as a
    /// singleton by <c>AddIrkallaKafka</c>.
    /// </summary>
    public sealed class ConsumerHealthState
    {
        /// <summary>A single consumer's health entry.</summary>
        public sealed record Entry(string Topic, ConsumerStatus Status, string? Error);

        private readonly ConcurrentDictionary<string, Entry> _entries = new();

        internal void Report(string id, string topic, ConsumerStatus status, string? error = null)
            => _entries[id] = new Entry(topic, status, error);

        /// <summary>Snapshot of all consumer entries.</summary>
        public IReadOnlyCollection<Entry> Entries => _entries.Values.ToArray();
    }
}
