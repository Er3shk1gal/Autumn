using System.Text;
using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Irkalla.Kafka.Exceptions;
using Irkalla.Kafka.Utils;
using Irkalla.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Irkalla.Kafka.MessageHandlers
{
    public abstract class BaseMessageHandler(
        KafkaProducer producer,
        IConsumer<string, byte[]> consumer,
        MessageHandlerConfig messageHandlerConfig,
        ILogger logger,
        IServiceProvider serviceProvider,
        Irkalla.Kafka.Configuration.IrkallaKafkaOptions options)
        : IDisposable
    {
        protected readonly KafkaProducer Producer = producer;
        protected readonly IConsumer<string, byte[]> Consumer = consumer;
        protected readonly MessageHandlerConfig MessageHandlerConfig = messageHandlerConfig;
        protected readonly ILogger Logger = logger;
        protected readonly IServiceProvider ServiceProvider = serviceProvider;
        protected readonly Irkalla.Kafka.Configuration.IrkallaKafkaOptions Options = options;

        private static readonly ActivitySource ActivitySource = new("Irkalla.Kafka");
        private static readonly Meter Meter = new("Irkalla.Kafka");
        private static readonly Counter<long> MessagesProcessedCounter = Meter.CreateCounter<long>("messages_processed");
        private static readonly Counter<long> MessagesFailedCounter = Meter.CreateCounter<long>("messages_failed");
        private static readonly Counter<long> MessagesDlqCounter = Meter.CreateCounter<long>("messages_dlq");
        private static readonly Counter<long> RetryAttemptsCounter = Meter.CreateCounter<long>("retry_attempts");
        private static readonly Histogram<double> ProcessingDurationHistogram = Meter.CreateHistogram<double>("processing_duration", "ms");

        public async Task Consume(CancellationToken cancellationToken = default)
        {
            var topicName = MessageHandlerConfig.RequestTopicConfig.TopicName;
            try
            {
                Consumer.Subscribe(topicName);
                Logger.LogInformation("Consumer subscribed to topic '{Topic}'", topicName);

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = Consumer.Consume(cancellationToken);
                        if (consumeResult == null || consumeResult.IsPartitionEOF)
                        {
                            continue;
                        }

                        bool success = false;
                        int attempts = 0;
                        Exception? lastException = null;

                        var stopwatch = Stopwatch.StartNew();
                        try
                        {
                            while (attempts <= Options.MaxRetries)
                            {
                                try
                                {
                                    if (await HandleMessage(consumeResult, cancellationToken))
                                    {
                                        success = true;
                                        break;
                                    }
                                }
                                catch (Exception ex) when (ex is KafkaConsumerException or KafkaConfigurationException)
                                {
                                    lastException = ex;
                                    break; // Deterministic exception - immediately apply ErrorPolicy
                                }
                                catch (Exception ex)
                                {
                                    lastException = ex;
                                    attempts++;
                                    if (attempts <= Options.MaxRetries)
                                    {
                                        Logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed for message on topic '{Topic}'. Retrying...",
                                            attempts, Options.MaxRetries, topicName);
                                        RetryAttemptsCounter.Add(1, new KeyValuePair<string, object?>("topic", topicName));

                                        // Clamp exponential back-off so a large MaxRetries cannot block
                                        // the poll loop past max.poll.interval.ms and get the member evicted.
                                        var delay = TimeSpan.FromMilliseconds(Math.Min(
                                            Options.RetryDelay.TotalMilliseconds * Math.Pow(2, attempts - 1),
                                            Options.MaxRetryDelay.TotalMilliseconds));
                                        await Task.Delay(delay, cancellationToken);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            stopwatch.Stop();
                            ProcessingDurationHistogram.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("topic", topicName));
                        }

                        if (success)
                        {
                            if (TryCommit(consumeResult))
                            {
                                MessagesProcessedCounter.Add(1, new KeyValuePair<string, object?>("topic", topicName));
                            }
                        }
                        else
                        {
                            lastException ??= new KafkaConsumerException("Handler returned false");
                            MessagesFailedCounter.Add(1, new KeyValuePair<string, object?>("topic", topicName));

                            // Apply error policy
                            switch (Options.ErrorPolicy)
                            {
                                case Configuration.ErrorPolicy.Skip:
                                    Logger.LogWarning("Skipping poison message on topic '{Topic}' due to Skip policy", topicName);
                                    TryCommit(consumeResult);
                                    break;

                                case Configuration.ErrorPolicy.Dlq:
                                    Logger.LogWarning("Sending poison message on topic '{Topic}' to DLQ due to Dlq policy", topicName);
                                    if (await TrySendToDlq(consumeResult, lastException))
                                    {
                                        MessagesDlqCounter.Add(1, new KeyValuePair<string, object?>("topic", topicName));
                                        TryCommit(consumeResult);
                                    }
                                    // DLQ publish failed → do NOT commit; message is redelivered later (at-least-once).
                                    break;

                                case Configuration.ErrorPolicy.Stop:
                                    Logger.LogCritical(lastException, "Fatal error processing message on topic '{Topic}'. Stopping consumer due to Stop policy.", topicName);
                                    ExceptionDispatchInfo.Capture(lastException).Throw();
                                    break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (ConsumeException ex) when (!ex.Error.IsFatal)
                    {
                        Logger.LogWarning(ex, "Transient Kafka consume error on topic '{Topic}', continuing", topicName);
                    }
                    catch (ConsumeException ex)
                    {
                        Logger.LogCritical(ex, "Fatal Kafka error on topic '{Topic}', stopping consumer", topicName);
                        throw; 
                    }
                }
            }
            finally
            {
                CloseConsumer();
            }
        }

        private bool TryCommit(ConsumeResult<string, byte[]> result)
        {
            try
            {
                Consumer.Commit(result);
                return true;
            }
            catch (Confluent.Kafka.KafkaException ex)
            {
                // Commit can fail after a rebalance (e.g. ILLEGAL_GENERATION). Never crash the
                // consumer over it — the offset is re-fetched and the message reprocessed by the
                // new partition owner.
                Logger.LogError(ex, "Offset commit failed on topic '{Topic}'; continuing (message may be redelivered)",
                    MessageHandlerConfig.RequestTopicConfig.TopicName);
                return false;
            }
        }

        private async Task<bool> TrySendToDlq(ConsumeResult<string, byte[]> result, Exception? exception)
        {
            try
            {
                await SendToDlq(result, exception);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to publish poison message to DLQ for topic '{Topic}'; will not commit",
                    MessageHandlerConfig.RequestTopicConfig.TopicName);
                return false;
            }
        }

        private async Task<bool> HandleMessage(ConsumeResult<string, byte[]> message, CancellationToken cancellationToken)
        {
            var headerBytes = message.Message.Headers?.FirstOrDefault(x => x.Key.Equals("method"));

            if (headerBytes == null)
            {
                throw new KafkaConsumerException("Message is missing required 'method' header");
            }

            var methodName = Encoding.UTF8.GetString(headerBytes.GetValueBytes());

            if (!MessageHandlerConfig.KafkaMethodExecutionConfigs.TryGetValue(methodName, out var config))
            {
                throw new KafkaConsumerException($"No handler registered for method '{methodName}'");
            }

            using var activity = StartConsumeActivity(message, methodName);

            var result = await InvokeServiceMethodAsync(config, message.Message.Value, cancellationToken);

            if (!config.RequireResponse || result == ServiceResolver.VoidResultMarker)
            {
                return true;
            }

            var senderName = config.KafkaServiceName ?? Options.ServiceName
                ?? throw new KafkaConfigurationException(
                    "KafkaServiceName is not configured for response handling globally or on the [KafkaService] attribute.");

            var responseMessage = new Message<string, byte[]>
            {
                Key = message.Message.Key,
                Value = result == null ? [] : await SerializeAsync(result, result.GetType(), new SerializationContext(MessageComponentType.Value, config.ResponseTopicConfig?.TopicName ?? "")),
                Headers =
                [
                    new Header("method", Encoding.UTF8.GetBytes(methodName)),
                    new Header("sender", Encoding.UTF8.GetBytes(senderName))
                ]
            };
            InjectTraceHeaders(responseMessage.Headers);

            return await SendResponse(config, responseMessage);
        }

        private async Task<object?> InvokeServiceMethodAsync(KafkaMethodExecutionConfig config, byte[] messageValue, CancellationToken cancellationToken)
        {
            using var scope = ServiceProvider.CreateScope();

            if (config.ServiceMethodPair.Parameters != null && config.ServiceMethodPair.Parameters.Any())
            {
                var parameters = new List<object>();
                var context = new SerializationContext(MessageComponentType.Value, MessageHandlerConfig.RequestTopicConfig.TopicName);
                
                bool hasMessagePayload = false;
                
                foreach (var paramInfo in config.ServiceMethodPair.Parameters)
                {
                    if (paramInfo.ParameterType == typeof(CancellationToken))
                    {
                        parameters.Add(cancellationToken);
                        continue;
                    }
                    
                    if (hasMessagePayload)
                    {
                        throw new KafkaConsumerException(
                            $"Method '{config.KafkaMethodName}' has multiple payload parameters. Only 1 payload parameter (and an optional CancellationToken) is allowed.");
                    }
                    
                    object? deserialized;
                    try
                    {
                        deserialized = await DeserializeAsync(messageValue, paramInfo.ParameterType, context);
                    }
                    catch (Exception ex)
                    {
                        throw new KafkaConsumerException($"Deserialization failed for parameter '{paramInfo.Name}': {ex.Message}", ex);
                    }
                    parameters.Add(deserialized!);
                    hasMessagePayload = true;
                }

                return await ServiceResolver.InvokeMethodAsync(
                    scope.ServiceProvider,
                    config.ServiceMethodPair.Method,
                    config.ServiceMethodPair.Service,
                    parameters);
            }

            return await ServiceResolver.InvokeMethodAsync(
                scope.ServiceProvider,
                config.ServiceMethodPair.Method,
                config.ServiceMethodPair.Service,
                null);
        }

        private async Task<bool> SendResponse(
            KafkaMethodExecutionConfig config,
            Message<string, byte[]> message)
        {
            if (config.ResponseTopicConfig == null || config.ResponseTopicPartition == null)
            {
                throw new KafkaConfigurationException(
                    $"Response topic is not configured for method '{config.KafkaMethodName}'. " +
                    "Set ResponseTopic on [KafkaService] attribute.");
            }

            return await Producer.ProduceAsync(
                config.ResponseTopicConfig,
                (int)config.ResponseTopicPartition,
                message);
        }

        private async Task SendToDlq(ConsumeResult<string, byte[]> consumeResult, Exception? exception)
        {
            var dlqTopic = MessageHandlerConfig.RequestTopicConfig.TopicName + Options.DlqTopicSuffix;
            
            var headers = consumeResult.Message.Headers ?? new Headers();
            
            // Remove existing error headers if this message has been retried across DLQs
            headers.Remove("error");
            headers.Remove("stacktrace");

            headers.Add("error", Encoding.UTF8.GetBytes(exception?.Message ?? "Unknown Error"));
            if (exception?.StackTrace != null)
            {
                headers.Add("stacktrace", Encoding.UTF8.GetBytes(exception.StackTrace));
            }
            InjectTraceHeaders(headers);

            var message = new Message<string, byte[]>
            {
                Key = consumeResult.Message.Key,
                Value = consumeResult.Message.Value,
                Headers = headers
            };

            var topicConfig = new TopicConfig
            {
                TopicName = dlqTopic,
                PartitionsCount = MessageHandlerConfig.RequestTopicConfig.PartitionsCount,
                ReplicationFactor = MessageHandlerConfig.RequestTopicConfig.ReplicationFactor
            };

            await Producer.ProduceAsync(topicConfig, -1, message);
        }

        private Activity? StartConsumeActivity(ConsumeResult<string, byte[]> message, string methodName)
        {
            string? traceParent = null;
            string? traceState = null;
            if (message.Message.Headers != null)
            {
                var tpHeader = message.Message.Headers.FirstOrDefault(h => h.Key == "traceparent");
                if (tpHeader != null) traceParent = Encoding.UTF8.GetString(tpHeader.GetValueBytes());
                
                var tsHeader = message.Message.Headers.FirstOrDefault(h => h.Key == "tracestate");
                if (tsHeader != null) traceState = Encoding.UTF8.GetString(tsHeader.GetValueBytes());
            }

            Activity? activity = null;
            if (traceParent != null && ActivityContext.TryParse(traceParent, traceState, out var parentContext))
            {
                activity = ActivitySource.StartActivity("irkalla.kafka.consume", ActivityKind.Consumer, parentContext);
            }
            else
            {
                activity = ActivitySource.StartActivity("irkalla.kafka.consume", ActivityKind.Consumer);
            }

            if (activity != null)
            {
                activity.SetTag("messaging.system", "kafka");
                activity.SetTag("messaging.destination.name", MessageHandlerConfig.RequestTopicConfig.TopicName);
                activity.SetTag("messaging.kafka.message.key", message.Message.Key);
                activity.SetTag("method", methodName);
            }

            return activity;
        }

        private void InjectTraceHeaders(Headers headers)
        {
            var currentActivity = Activity.Current;
            if (currentActivity != null && currentActivity.Id != null)
            {
                headers.Remove("traceparent");
                headers.Add("traceparent", Encoding.UTF8.GetBytes(currentActivity.Id));
                if (currentActivity.TraceStateString != null)
                {
                    headers.Remove("tracestate");
                    headers.Add("tracestate", Encoding.UTF8.GetBytes(currentActivity.TraceStateString));
                }
            }
        }

        protected abstract Task<object?> DeserializeAsync(byte[] bytes, Type targetType, SerializationContext context);
        
        protected abstract Task<byte[]> SerializeAsync(object obj, Type targetType, SerializationContext context);

        private readonly object _closeLock = new();
        private bool _closed;

        // Closes the consumer exactly once. Both the exiting consume loop (its finally) and Dispose
        // may call this, potentially from different threads; IConsumer is not thread-safe, so the
        // close is serialized and idempotent.
        private void CloseConsumer()
        {
            lock (_closeLock)
            {
                if (_closed) return;
                _closed = true;
                try
                {
                    Consumer.Close();
                    Logger.LogInformation("Consumer closed for topic '{Topic}'",
                        MessageHandlerConfig.RequestTopicConfig.TopicName);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error closing consumer for topic '{Topic}'",
                        MessageHandlerConfig.RequestTopicConfig.TopicName);
                }
            }
        }

        public void Dispose()
        {
            CloseConsumer();
            try
            {
                Consumer.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error disposing consumer for topic '{Topic}'", MessageHandlerConfig.RequestTopicConfig.TopicName);
            }

            GC.SuppressFinalize(this);
        }
    }
}
