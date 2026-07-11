using System.Reflection;
using Autumn.Kafka.Configuration;
using Autumn.Kafka.Hosting;
using Autumn.Kafka.MessageHandlers;
using Autumn.Kafka.Utils;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Autumn.Kafka.Extensions
{
    /// <summary>
    /// Extension methods for registering Autumn.Kafka services in the DI container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Autumn.Kafka services to the dependency injection container.
        /// Scans the assembly for <c>[KafkaService]</c> classes and registers
        /// one background consumer per unique topic.
        /// <para>
        /// Usage:
        /// <code>
        /// services.AddAutumnKafka(options =>
        /// {
        ///     options.BootstrapServers = "localhost:9092";
        ///     options.GroupId = "my-consumer-group";
        ///     options.ServiceName = "my-service";
        ///     options.ServiceAssembly = typeof(Program).Assembly;
        /// });
        /// </code>
        /// </para>
        /// </summary>
        public static IServiceCollection AddAutumnKafka(
            this IServiceCollection services,
            Action<AutumnKafkaOptions> configure)
        {
            var options = new AutumnKafkaOptions();
            configure(options);

            if (string.IsNullOrWhiteSpace(options.GroupId))
            {
                throw new ArgumentException("GroupId is required in AutumnKafkaOptions.");
            }

            // Register options as singleton
            services.AddSingleton(options);

            // Register Kafka infrastructure
            RegisterKafkaInfrastructure(services, options);

            // Scan assembly and register per-topic consumers
            RegisterConsumerHostedServices(services, options);

            return services;
        }

        private static void RegisterKafkaInfrastructure(
            IServiceCollection services, AutumnKafkaOptions options)
        {
            // Admin client (singleton — thread-safe)
            services.AddSingleton<IAdminClient>(_ =>
                new AdminClientBuilder(options.BuildAdminClientConfig()).Build());

            if (!string.IsNullOrWhiteSpace(options.SchemaRegistryUrl))
            {
                services.AddSingleton<Confluent.SchemaRegistry.ISchemaRegistryClient>(_ =>
                    new Confluent.SchemaRegistry.CachedSchemaRegistryClient(options.BuildSchemaRegistryConfig()));
            }

            // Producer (singleton — thread-safe)
            services.AddSingleton<IProducer<string, byte[]>>(_ =>
                new ProducerBuilder<string, byte[]>(options.BuildProducerConfig()).Build());

            // Consumer factory — each hosted service creates and OWNS its own consumer. Registering
            // the consumer itself as a transient resolved from the root provider would make the DI
            // container track it as a root-scoped disposable and retain the handle until app exit
            // (multiplied by ConsumerMode.Auto). A factory hands ownership to the hosted service.
            services.AddSingleton<Func<IConsumer<string, byte[]>>>(sp =>
                () => new ConsumerBuilder<string, byte[]>(
                    sp.GetRequiredService<AutumnKafkaOptions>().BuildConsumerConfig()).Build());

            // Internal services
            services.AddSingleton<KafkaTopicManager>();
            services.AddSingleton<KafkaProducer>();
        }

        private static void RegisterConsumerHostedServices(
            IServiceCollection services, AutumnKafkaOptions options)
        {
            var assembly = options.ServiceAssembly
                ?? Assembly.GetEntryAssembly()
                ?? throw new InvalidOperationException(
                    "Unable to determine entry assembly. Set AutumnKafkaOptions.ServiceAssembly explicitly.");

            var handlerConfigs = MessageHandlerFactory.BuildHandlerConfigs(assembly).ToList();

            if (handlerConfigs.Count == 0)
            {
                return;
            }

            var hasBinaryHandlers = handlerConfigs.Any(c => 
                c.HandlerType == Attributes.MessageHandlerType.AVRO || 
                c.HandlerType == Attributes.MessageHandlerType.PROTOBUF);

            if (hasBinaryHandlers && string.IsNullOrWhiteSpace(options.SchemaRegistryUrl))
            {
                throw new Exceptions.KafkaConfigurationException(
                    "SchemaRegistryUrl is required in AutumnKafkaOptions when using AVRO or PROTOBUF handlers.");
            }

            // Register consumers per unique topic. In Single mode: one consumer per topic.
            // In Auto mode: several consumers in the same group (up to the partition count),
            // so Kafka spreads the topic's partitions across them for in-process parallelism.
            foreach (var config in handlerConfigs)
            {
                var capturedConfig = config;
                var instances = ResolveConsumerCount(options, capturedConfig);

                for (var i = 0; i < instances; i++)
                {
                    services.AddSingleton<IHostedService>(sp =>
                        new KafkaConsumerHostedService(
                            capturedConfig,
                            sp,
                            sp.GetRequiredService<ILogger<KafkaConsumerHostedService>>()));
                }
            }
        }

        private static int ResolveConsumerCount(
            AutumnKafkaOptions options, Utils.Models.MessageHandlerConfig config)
        {
            if (options.ConsumerMode != ConsumerMode.Auto)
            {
                return 1;
            }

            // Never exceed the partition count — extra consumers in the group would sit idle.
            var partitions = Math.Max(1, config.RequestTopicConfig.PartitionsCount);
            var cap = options.MaxConsumersPerTopic > 0
                ? Math.Min(partitions, options.MaxConsumersPerTopic)
                : partitions;
            return Math.Max(1, cap);
        }
    }
}
