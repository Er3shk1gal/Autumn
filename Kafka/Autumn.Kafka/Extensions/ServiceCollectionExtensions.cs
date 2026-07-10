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

            // Producer (singleton — thread-safe)
            services.AddSingleton<IProducer<string, string>>(_ =>
                new ProducerBuilder<string, string>(options.BuildProducerConfig()).Build());

            // Consumer factory — each topic handler gets its own consumer
            services.AddTransient<IConsumer<string, string>>(_ =>
                new ConsumerBuilder<string, string>(options.BuildConsumerConfig()).Build());

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

            // Register one hosted service per unique topic
            foreach (var config in handlerConfigs)
            {
                var capturedConfig = config;

                services.AddSingleton<IHostedService>(sp =>
                    new KafkaConsumerHostedService(
                        capturedConfig,
                        sp,
                        sp.GetRequiredService<AutumnKafkaOptions>(),
                        sp.GetRequiredService<ILogger<KafkaConsumerHostedService>>()));
            }
        }
    }
}
