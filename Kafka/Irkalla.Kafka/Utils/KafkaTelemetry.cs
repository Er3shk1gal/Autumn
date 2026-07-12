using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Confluent.Kafka;

namespace Irkalla.Kafka.Utils
{
    /// <summary>
    /// Shared OpenTelemetry primitives for Irkalla.Kafka. A single <see cref="ActivitySource"/> and
    /// <see cref="Meter"/> (both named <see cref="SourceName"/>) back every produce/consume span and
    /// metric, so one <c>AddSource("Irkalla.Kafka")</c> / <c>AddMeter("Irkalla.Kafka")</c> wires up the
    /// whole library — producer, RPC client and consumer alike.
    /// </summary>
    public static class KafkaTelemetry
    {
        /// <summary>Name of both the <see cref="ActivitySource"/> and the <see cref="Meter"/>.</summary>
        public const string SourceName = "Irkalla.Kafka";

        public static readonly ActivitySource ActivitySource = new(SourceName);
        public static readonly Meter Meter = new(SourceName);

        // Producer-side instruments. Consumer-side instruments live in BaseMessageHandler but are
        // created from this same Meter, so all metrics surface under the one "Irkalla.Kafka" meter.
        private static readonly Counter<long> MessagesProducedCounter =
            Meter.CreateCounter<long>("messages_produced");
        private static readonly Counter<long> MessagesProduceFailedCounter =
            Meter.CreateCounter<long>("messages_produce_failed");
        private static readonly Histogram<double> ProduceDurationHistogram =
            Meter.CreateHistogram<double>("produce_duration", "ms");

        /// <summary>
        /// Starts a producer span for a message about to be sent to <paramref name="topic"/> and injects
        /// W3C trace context (traceparent/tracestate) into <paramref name="headers"/> so the downstream
        /// consumer continues the same trace. Returns the started activity (null when nothing is
        /// listening); the caller disposes it once the send completes.
        /// </summary>
        public static Activity? StartProduce(string topic, string? method, Headers headers)
        {
            var activity = ActivitySource.StartActivity("irkalla.kafka.produce", ActivityKind.Producer);
            if (activity != null)
            {
                activity.SetTag("messaging.system", "kafka");
                activity.SetTag("messaging.operation", "publish");
                activity.SetTag("messaging.destination.name", topic);
                if (method != null) activity.SetTag("method", method);
            }

            // Inject AFTER StartActivity so the produce span (now Activity.Current) becomes the parent
            // the consumer links to. When no listener is registered the activity is null and the ambient
            // Activity.Current (e.g. an inbound HTTP span) is propagated instead — still correct.
            InjectTraceHeaders(headers);
            return activity;
        }

        /// <summary>
        /// Writes the current <see cref="Activity"/>'s W3C trace context into Kafka headers so a
        /// downstream consumer links its span to this trace. No-op when there is no ambient activity.
        /// </summary>
        public static void InjectTraceHeaders(Headers headers)
        {
            var current = Activity.Current;
            if (current?.Id == null) return;

            headers.Remove("traceparent");
            headers.Add("traceparent", Encoding.UTF8.GetBytes(current.Id));
            if (current.TraceStateString != null)
            {
                headers.Remove("tracestate");
                headers.Add("tracestate", Encoding.UTF8.GetBytes(current.TraceStateString));
            }
        }

        public static void RecordProduced(string topic) =>
            MessagesProducedCounter.Add(1, new KeyValuePair<string, object?>("topic", topic));

        public static void RecordProduceFailed(string topic) =>
            MessagesProduceFailedCounter.Add(1, new KeyValuePair<string, object?>("topic", topic));

        public static void RecordProduceDuration(double milliseconds, string topic) =>
            ProduceDurationHistogram.Record(milliseconds, new KeyValuePair<string, object?>("topic", topic));
    }
}
