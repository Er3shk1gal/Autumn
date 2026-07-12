using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Utils;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Irkalla.Kafka.Rpc
{
    /// <summary>
    /// Request/reply RPC client. A single reply consumer manually assigns every partition of the
    /// reply topic at the end offset (no consumer group → no rebalance) and routes each reply to the
    /// awaiting call via a correlation-id pending map. A sweeper enforces per-call deadlines and a
    /// semaphore bounds in-flight calls.
    /// </summary>
    public sealed class KafkaRpcClient : BackgroundService, IKafkaRpcClient
    {
        private sealed class PendingCall
        {
            public readonly TaskCompletionSource<byte[]> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public long DeadlineTicks;
        }

        private readonly IProducer<string, byte[]> _producer;
        private readonly IAdminClient _admin;
        private readonly KafkaTopicManager _topics;
        private readonly IrkallaKafkaOptions _options;
        private readonly KafkaRpcOptions _rpc;
        private readonly ILogger<KafkaRpcClient> _logger;

        private readonly string _replyTopic;
        private readonly string _rpcGroup;
        private readonly ConcurrentDictionary<string, PendingCall> _pending = new();
        private readonly SemaphoreSlim _inFlight;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private IConsumer<string, byte[]>? _consumer;

        public KafkaRpcClient(
            IProducer<string, byte[]> producer,
            IAdminClient admin,
            KafkaTopicManager topics,
            IrkallaKafkaOptions options,
            KafkaRpcOptions rpc,
            ILogger<KafkaRpcClient> logger)
        {
            _producer = producer;
            _admin = admin;
            _topics = topics;
            _options = options;
            _rpc = rpc;
            _logger = logger;
            _replyTopic = rpc.ReplyTopic ?? $"{options.GroupId}.replies";
            _rpcGroup = $"{options.GroupId}-rpc-{Guid.NewGuid():N}";
            _inFlight = new SemaphoreSlim(rpc.MaxInFlightCalls, rpc.MaxInFlightCalls);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _topics.CreateTopicAsync(_replyTopic, _rpc.ReplyTopicPartitions, 1);

            _consumer = new ConsumerBuilder<string, byte[]>(_options.BuildConsumerConfig(_rpcGroup)).Build();

            // No consumer group → no rebalance. Retry: metadata for a just-created topic may not
            // list partitions immediately.
            var partitionIds = new List<int>();
            for (var attempt = 0; attempt < 20 && !stoppingToken.IsCancellationRequested; attempt++)
            {
                var meta = _admin.GetMetadata(_replyTopic, TimeSpan.FromSeconds(5));
                var topicMeta = meta.Topics.FirstOrDefault(t => t.Topic == _replyTopic);
                if (topicMeta is { Error.Code: ErrorCode.NoError } && topicMeta.Partitions.Count > 0)
                {
                    partitionIds = topicMeta.Partitions.Select(p => p.PartitionId).ToList();
                    break;
                }
                await Task.Delay(250, stoppingToken);
            }

            if (partitionIds.Count == 0)
            {
                _logger.LogError("RPC reply topic '{Topic}' has no partitions to assign", _replyTopic);
                _ready.TrySetException(new KafkaRpcClientClosedException(
                    $"RPC reply topic '{_replyTopic}' unavailable."));
                return;
            }

            // Pin each partition to its CURRENT high watermark (concrete offset). Assigning the
            // logical Offset.End resolves lazily on first fetch and can skip a reply produced right
            // after assignment; a concrete offset cannot.
            var assignment = new List<TopicPartitionOffset>();
            foreach (var pid in partitionIds)
            {
                var tp = new TopicPartition(_replyTopic, new Partition(pid));
                var wm = _consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(5));
                assignment.Add(new TopicPartitionOffset(tp, new Offset(wm.High)));
            }
            _consumer.Assign(assignment);
            _logger.LogInformation("RPC assigned {Count} partition(s) of reply topic '{Topic}'",
                assignment.Count, _replyTopic);

            _ready.TrySetResult();
            _ = Task.Run(() => SweepLoopAsync(stoppingToken), stoppingToken);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<string, byte[]> cr;
                    try
                    {
                        cr = _consumer.Consume(stoppingToken);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ConsumeException ex)
                    {
                        _logger.LogWarning(ex, "RPC reply consume error on '{Topic}'", _replyTopic);
                        continue;
                    }

                    if (cr == null || cr.IsPartitionEOF) continue;

                    var corr = cr.Message.Headers?.FirstOrDefault(h => h.Key == "correlation-id");
                    if (corr == null) continue;

                    var id = Encoding.UTF8.GetString(corr.GetValueBytes());
                    if (_pending.TryRemove(id, out var pending))
                    {
                        pending.Tcs.TrySetResult(cr.Message.Value ?? Array.Empty<byte>());
                        _inFlight.Release();
                    }
                    // else: orphan (late/duplicate/another instance's reply) — ignore.
                }
            }
            finally
            {
                FaultAllPending(new KafkaRpcClientClosedException("RPC client stopped."));
                try { _consumer.Close(); } catch { /* ignore */ }
            }
        }

        private async Task SweepLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_rpc.SweepInterval, ct).ConfigureAwait(false);
                    var now = Environment.TickCount64;
                    foreach (var kv in _pending)
                    {
                        if (kv.Value.DeadlineTicks <= now && _pending.TryRemove(kv.Key, out var p))
                        {
                            p.Tcs.TrySetException(new KafkaRpcTimeoutException(
                                $"RPC call '{kv.Key}' timed out after {_rpc.DefaultTimeout}."));
                            _inFlight.Release();
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* stopping */ }
        }

        private void FaultAllPending(Exception ex)
        {
            foreach (var id in _pending.Keys.ToArray())
            {
                if (_pending.TryRemove(id, out var p))
                {
                    p.Tcs.TrySetException(ex);
                    _inFlight.Release();
                }
            }
        }

        public async Task<TResponse?> CallAsync<TRequest, TResponse>(
            string topic,
            string method,
            TRequest request,
            TimeSpan? timeout = null,
            string? key = null,
            CancellationToken cancellationToken = default)
        {
            await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            var to = timeout ?? _rpc.DefaultTimeout;
            await _inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);

            var corrId = Guid.NewGuid().ToString("N");
            var pending = new PendingCall { DeadlineTicks = Environment.TickCount64 + (long)to.TotalMilliseconds };
            _pending[corrId] = pending;

            try
            {
                var headers = new Headers
                {
                    { "method", Encoding.UTF8.GetBytes(method) },
                    { "correlation-id", Encoding.UTF8.GetBytes(corrId) },
                    { "reply-to", Encoding.UTF8.GetBytes(_replyTopic) },
                };
                if (_options.ServiceName != null)
                    headers.Add("sender", Encoding.UTF8.GetBytes(_options.ServiceName));

                // Producer span + trace-context injection so the RPC request→server hop stays on the
                // same distributed trace (the reply carries correlation-id, matched by the consumer above).
                using var activity = KafkaTelemetry.StartProduce(topic, method, headers);

                var message = new Message<string, byte[]>
                {
                    Key = key ?? corrId,
                    Value = JsonSerializer.SerializeToUtf8Bytes(request, _options.JsonSerializerOptions),
                    Headers = headers,
                };

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await _producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
                    KafkaTelemetry.RecordProduced(topic);
                }
                catch
                {
                    KafkaTelemetry.RecordProduceFailed(topic);
                    activity?.SetStatus(ActivityStatusCode.Error);
                    throw;
                }
                finally
                {
                    KafkaTelemetry.RecordProduceDuration(stopwatch.Elapsed.TotalMilliseconds, topic);
                }

                await using var reg = cancellationToken.Register(() =>
                {
                    if (_pending.TryRemove(corrId, out var p))
                    {
                        p.Tcs.TrySetCanceled(cancellationToken);
                        _inFlight.Release();
                    }
                });

                var bytes = await pending.Tcs.Task.ConfigureAwait(false);
                return bytes.Length == 0
                    ? default
                    : JsonSerializer.Deserialize<TResponse>(bytes, _options.JsonSerializerOptions);
            }
            catch
            {
                if (_pending.TryRemove(corrId, out _)) _inFlight.Release();
                throw;
            }
        }

        public override void Dispose()
        {
            _consumer?.Dispose();
            base.Dispose();
        }
    }
}
