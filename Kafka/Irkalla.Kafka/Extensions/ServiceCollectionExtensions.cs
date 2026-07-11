using System.Reflection;
using Irkalla.Kafka.Configuration;
using Irkalla.Kafka.Deduplication;
using Irkalla.Kafka.Hosting;
using Irkalla.Kafka.MessageHandlers;
using Irkalla.Kafka.Producing;
using Irkalla.Kafka.Rpc;
using Irkalla.Kafka.Utils;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Irkalla.Kafka.Extensions
{
    /// <summary>
    /// Extension methods for registering Irkalla.Kafka services in the DI container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Irkalla.Kafka services to the dependency injection container.
        /// Scans the assembly for <c>[KafkaService]</c> classes and registers
        /// one background consumer per unique topic.
        /// <para>
        /// Usage:
        /// <code>
        /// services.AddIrkallaKafka(options =>
        /// {
        ///     options.BootstrapServers = "localhost:9092";
        ///     options.GroupId = "my-consumer-group";
        ///     options.ServiceName = "my-service";
        ///     options.ServiceAssembly = typeof(Program).Assembly;
        /// });
        /// </code>
        /// </para>
        /// </summary>
        public static IServiceCollection AddIrkallaKafka(
            this IServiceCollection services,
            Action<IrkallaKafkaOptions> configure)
        {
            var options = new IrkallaKafkaOptions();
            configure(options);

            if (string.IsNullOrWhiteSpace(options.GroupId))
            {
                throw new ArgumentException("GroupId is required in IrkallaKafkaOptions.");
            }

            // Register options as singleton (+ IOptions view over the same snapshot)
            services.AddSingleton(options);
            services.AddSingleton<IOptions<IrkallaKafkaOptions>>(Options.Create(options));

            // Register Kafka infrastructure
            RegisterKafkaInfrastructure(services, options);

            // Scan assembly and register per-topic consumers
            RegisterConsumerHostedServices(services, options);

            return services;
        }

        /// <summary>
        /// Adds Irkalla.Kafka, binding options from configuration (section
        /// <see cref="IrkallaKafkaOptions.SectionName"/> = "IrkallaKafka" by default), then applying
        /// the optional <paramref name="configure"/> callback so code can override appsettings.
        /// <para>
        /// appsettings.json:
        /// <code>
        /// {
        ///   "IrkallaKafka": {
        ///     "BootstrapServers": "broker:9092",
        ///     "GroupId": "billing",
        ///     "ErrorPolicy": "Dlq",
        ///     "ConsumerMode": "Auto",
        ///     "Security": { "SecurityProtocol": "SaslSsl", "SaslMechanism": "Plain" }
        ///   }
        /// }
        /// </code>
        /// </para>
        /// </summary>
        public static IServiceCollection AddIrkallaKafka(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<IrkallaKafkaOptions>? configure = null)
        {
            var section = configuration.GetSection(IrkallaKafkaOptions.SectionName);
            return services.AddIrkallaKafka(options =>
            {
                section.Bind(options);          // appsettings first
                configure?.Invoke(options);     // code overrides last
            });
        }

        /// <summary>
        /// Registers a <b>producer-only</b> Irkalla.Kafka setup: <see cref="IKafkaProducer"/> for
        /// sending messages to Irkalla services, without scanning for <c>[KafkaService]</c> classes
        /// or starting any consumer. No <c>GroupId</c> is required — nothing is consumed.
        /// <para>
        /// Usage (e.g. an ASP.NET app that only sends):
        /// <code>
        /// services.AddIrkallaKafkaProducer(o => o.BootstrapServers = "localhost:9092");
        /// // then: await producer.SendAsync("orders-request", "CreateOrder", request);
        /// </code>
        /// </para>
        /// </summary>
        public static IServiceCollection AddIrkallaKafkaProducer(
            this IServiceCollection services,
            Action<IrkallaKafkaOptions> configure)
        {
            var options = new IrkallaKafkaOptions();
            configure(options);

            services.AddSingleton(options);
            services.AddSingleton<IOptions<IrkallaKafkaOptions>>(Options.Create(options));
            services.AddSingleton<IProducer<string, byte[]>>(_ =>
                new ProducerBuilder<string, byte[]>(options.BuildProducerConfig()).Build());
            services.TryAddSingleton<IKafkaProducer, KafkaMessageProducer>();

            return services;
        }

        /// <summary>
        /// Registers the request/reply <see cref="IKafkaRpcClient"/>. Call after
        /// <c>AddIrkallaKafka</c> or <c>AddIrkallaKafkaProducer</c> (which configure
        /// <see cref="IrkallaKafkaOptions"/>); the required producer / admin / topic-manager are
        /// added here if not already present.
        /// <para>
        /// <code>
        /// services.AddIrkallaKafkaProducer(o => { o.BootstrapServers = "..."; o.GroupId = "web"; });
        /// services.AddIrkallaKafkaRpcClient();
        /// // var res = await rpc.CallAsync&lt;CreateOrder, OrderResult&gt;("orders-request", "CreateOrder", req);
        /// </code>
        /// </para>
        /// </summary>
        public static IServiceCollection AddIrkallaKafkaRpcClient(
            this IServiceCollection services,
            Action<KafkaRpcOptions>? configure = null)
        {
            var rpc = new KafkaRpcOptions();
            configure?.Invoke(rpc);
            services.AddSingleton(rpc);

            services.TryAddSingleton<IAdminClient>(sp =>
                new AdminClientBuilder(sp.GetRequiredService<IrkallaKafkaOptions>().BuildAdminClientConfig()).Build());
            services.TryAddSingleton<IProducer<string, byte[]>>(sp =>
                new ProducerBuilder<string, byte[]>(sp.GetRequiredService<IrkallaKafkaOptions>().BuildProducerConfig()).Build());
            services.TryAddSingleton<KafkaTopicManager>();

            services.AddSingleton<KafkaRpcClient>();
            services.AddSingleton<IKafkaRpcClient>(sp => sp.GetRequiredService<KafkaRpcClient>());
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<KafkaRpcClient>());
            return services;
        }

        /// <summary>
        /// Registers the in-memory <see cref="IMessageDeduplicator"/> so consumers skip messages whose
        /// <c>message-id</c> header was already processed. In-memory state is per-process and not
        /// durable — for production across restarts/instances, register your own
        /// <see cref="IMessageDeduplicator"/> over a shared store instead.
        /// </summary>
        public static IServiceCollection AddIrkallaKafkaInMemoryDeduplicator(
            this IServiceCollection services, int capacity = 100_000)
        {
            services.TryAddSingleton<IMessageDeduplicator>(new InMemoryMessageDeduplicator(capacity));
            return services;
        }

        private static void RegisterKafkaInfrastructure(
            IServiceCollection services, IrkallaKafkaOptions options)
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
            services.AddSingleton<Func<string?, IConsumer<string, byte[]>>>(sp =>
                groupId => new ConsumerBuilder<string, byte[]>(
                    sp.GetRequiredService<IrkallaKafkaOptions>().BuildConsumerConfig(groupId)).Build());

            // Internal services
            services.AddSingleton<KafkaTopicManager>();
            services.AddSingleton<KafkaProducer>();

            // Public typed producer
            services.TryAddSingleton<IKafkaProducer, KafkaMessageProducer>();

            // Health: consumer status registry + the health check (wire via AddHealthChecks().AddCheck<>())
            services.TryAddSingleton<ConsumerHealthState>();
            services.TryAddSingleton<HealthChecks.IrkallaKafkaHealthCheck>();
        }

        private static void RegisterConsumerHostedServices(
            IServiceCollection services, IrkallaKafkaOptions options)
        {
            var assemblies = options.ServiceAssemblies is { Length: > 0 }
                ? options.ServiceAssemblies
                : new[]
                {
                    options.ServiceAssembly
                        ?? Assembly.GetEntryAssembly()
                        ?? throw new InvalidOperationException(
                            "Unable to determine entry assembly. Set IrkallaKafkaOptions.ServiceAssembly or ServiceAssemblies explicitly.")
                };

            var handlerConfigs = MessageHandlerFactory.BuildHandlerConfigs(assemblies).ToList();

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
                    "SchemaRegistryUrl is required in IrkallaKafkaOptions when using AVRO or PROTOBUF handlers.");
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
            IrkallaKafkaOptions options, Utils.Models.MessageHandlerConfig config)
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
