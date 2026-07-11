# Changelog

All notable changes to this project will be documented in this file.

## [0.1.0] - 2026-07-10

### Added
- **xUnit Test Project**: Added `Autumn.Kafka.Tests` covering configuration scanning and service resolution.
- **Integration Tests**: Added dockerized integration tests using `Testcontainers.Kafka` verifying JSON roundtrip, DLQ handling, and host crashes on stop policy.
- **Observability (Metrics & Tracing)**:
  - Added OpenTelemetry distributed tracing using `ActivitySource("Autumn.Kafka")` propagating `traceparent` headers.
  - Added metrics using `Meter("Autumn.Kafka")` tracking message execution counts, durations, failures, DLQ publish, and retries.
- **Exponential Backoff**: Added retry options to `AutumnKafkaOptions` containing `RetryDelay` supporting exponential backoff.
- **Treat Warnings As Errors**: Enforced strict compilation quality globally via `Directory.Build.props`.

### Changed
- **Consumer Hosted Service Lifecycle**: Awaiting `Task.Run` for the consumption loop in `ExecuteAsync` to prevent zombie-process issues and allow hosted service crashes to terminate the host. Added proper resource disposal.
- **Error Semantics**:
  - Partition `< 0` resolved to `Partition.Any` in `KafkaProducer.ProduceAsync`.
  - Cache created topics in `KafkaTopicManager` to avoid repeated metadata lookups.
  - Unwrapped TargetInvocationException in `ServiceResolver` to expose exact underlying errors.
- **Fail Fast Configuration**:
  - Throwing `KafkaConfigurationException` if `EnableAutoCommit` is set to `true`.
  - Enforced single-interface fallback check in `ServiceResolver.ResolveService`.

### Fixed
- Fixed zombie hosted service state by awaiting task execute.
- Fixed stack trace losses by using `ExceptionDispatchInfo.Capture`.
