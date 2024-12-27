using System.Text;
using Autumn.Kafka.Exceptions.ConsumerExceptions;
using Autumn.Kafka.Exceptions.ProducerExceptions;
using Autumn.Kafka.Utils;
using Autumn.Kafka.Utils.Models;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Autumn.Kafka.MessageHandlers
{
    public class JsonMessageHandler
    {
        private readonly KafkaProducer _producer;
        private readonly IConsumer<string,string> _consumer;
        private readonly TopicConfig _requestTopicConfig;
        private readonly MessageHandlerConfig _messageHandlerConfig;
        private readonly ILogger<JsonMessageHandler> _logger;
        private readonly IServiceProvider _serviceProvider;
        public JsonMessageHandler(KafkaProducer producer, IConsumer<string, string> consumer, TopicConfig requestTopicConfig, MessageHandlerConfig messageHandlerConfig, ILogger<JsonMessageHandler> logger, IServiceProvider serviceProvider)
        {
            _producer = producer;
            _consumer = consumer;
            _requestTopicConfig = requestTopicConfig;
            _messageHandlerConfig = messageHandlerConfig;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }
        public async Task Consume()
        {
            try
            {
                _consumer.Assign(GetPartitions());
                while (true)
                {
                    try
                    {
                        ConsumeResult<string,string> message = _consumer.Consume();
                        if(await HandleMessage(message))
                        {
                            _consumer.Commit();
                        }
                        throw new HandleMethodException("0_0");
                    }
                    catch(Exception ex)
                    {
                        _logger.LogError(ex, ex.Message);
                        _consumer.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                _consumer.Commit();
            }
        }
        private List<TopicPartition> GetPartitions()
        {
            var partitions = new List<TopicPartition>();
            for (int partition = 0; partition < _requestTopicConfig.PartitionsCount; partition++)
            {
                partitions.Add(new TopicPartition(_requestTopicConfig.TopicName, partition));
            }
            return partitions;
        }
        public async Task<bool> HandleMessage(ConsumeResult<string,string> message)
        {
            var headerBytes = message.Message.Headers
                        .FirstOrDefault(x => x.Key.Equals("method"));
            if (headerBytes != null)
            {
                var methodString = Encoding.UTF8.GetString(headerBytes.GetValueBytes());
                if(_messageHandlerConfig.kafkaMethodExecutionConfigs.Any(x=>x.KafkaMethodName.Equals(methodString)))
                {
                    KafkaMethodExecutionConfig config = _messageHandlerConfig.kafkaMethodExecutionConfigs.FirstOrDefault(x=>x.KafkaMethodName.Equals(methodString)) ?? throw new MethodInvalidException("Invalid method name");
                    if (config != null)
                    {
                        object result;
                        if(config.ServiceMethodPair.Parameter!=null)
                        {
                            Type type =config.ServiceMethodPair.Parameter.GetType();
                            result = ServiceResolver.InvokeMethodByHeader(_serviceProvider, config.ServiceMethodPair.Method, config.ServiceMethodPair.Service, JsonConvert.DeserializeObject(message.Message.Value, type));
                        }
                        else
                        {
                            result = ServiceResolver.InvokeMethodByHeader(_serviceProvider, config.ServiceMethodPair.Method, config.ServiceMethodPair.Service, null);
                        }
                        if(config.RequireResponse)
                        {
                            if(config.KafkaServiceName!=null)
                            {
                                return await SendResponse(config,new Message<string, string>(){
                                Key = message.Message.Key,
                                    Value = JsonConvert.SerializeObject(result),
                                    Headers = [
                                        new Header("method",Encoding.UTF8.GetBytes(methodString)),
                                        new Header("sender",Encoding.UTF8.GetBytes(config.KafkaServiceName))
                                    ]
                                });
                            }
                            return await SendResponse(config,new Message<string, string>(){
                                Key = message.Message.Key,
                                    Value = JsonConvert.SerializeObject(result),
                                    Headers = [
                                        new Header("method",Encoding.UTF8.GetBytes(methodString)),
                                        new Header("sender",Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("GLOBAL_SERVICE_NAME") ?? throw new ConfigInvalidException("Invalid config")))
                                    ]
                                });
                        }
                        else
                        {
                            return true;
                        }
                    }
                    throw new ConfigInvalidException("Invalid config");
                }
                throw new MethodInvalidException("Invalid method name");
            }
            throw new HeaderBytesNullException("Header bytes are null");
        }
        private async Task<bool> SendResponse(KafkaMethodExecutionConfig config, Message<string,string> message)
        {
            try
            {
                if(config.responseTopicPartition != null && config.responseTopicConfig != null)
                {
                    return await _producer.ProduceAsync(config.responseTopicConfig, (int)config.responseTopicPartition, message);
                }
                throw new ProducerException("Failed to send response");
            }
            catch
            {
                return false;
            }
        }
    }
}