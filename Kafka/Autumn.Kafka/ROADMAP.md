# Autumn.Kafka — дорожная карта до 1.0

Как дописываем библиотеку: от текущего pre-release (0.0.2) до стабильного 1.0 на NuGet. Пять релизных вех, каждая — самостоятельно ценная и релизится отдельно.

> Детальный технический разбор задач ядра — в [IMPROVEMENT_PLAN.md](./IMPROVEMENT_PLAN.md). Этот файл — карта верхнего уровня: что, в каком порядке, к какой версии, с какими критериями «готово».
>
> **Позиционирование (держим в голове на каждом решении):** лёгкий attribute-first Kafka-фреймворк с первоклассным request/reply. Аналог `@KafkaListener` + `ReplyingKafkaTemplate` из Spring. **Не** общая абстракция обмена сообщениями (это MassTransit/Wolverine). Каждую фичу проверяем: «это делает нас лучше в request/reply-нише или тянет в сторону тяжёлого брокера?»

---

## Текущее состояние (0.1.0 фактически готов)

**Есть:** серверная сторона — `[KafkaService]`/`[KafkaMethod]`, роутинг по header `method`, публикация ответов, JSON/Avro/Protobuf, hosted service на топик, consumer group, ручной commit, ErrorPolicy (Skip/Dlq/Stop) + retry с backoff, fail-fast валидация на старте, авто-создание топиков, наблюдаемость (ActivitySource + Meter + traceparent). Lifecycle-регрессия закрыта. `EnableAutoCommit` запрещён. Сборка чистая.

**Тесты: 26, все зелёные** — 23 unit (матрица валидации `MessageHandlerFactory`, retry-классификация, `ServiceResolver` включая формы возврата) + 3 интеграционных на Testcontainers (JSON roundtrip, poison→DLQ, Stop→останов хоста).

**Пофикшен баг (найден новым тестом):** `ServiceResolver` возвращал внутренний `VoidTaskResult` вместо `VoidResultMarker` для `async Task`-хендлеров (`Task.CompletedTask` имеет рантайм-тип `Task<VoidTaskResult>`). Гейт переведён на декларированный тип возврата метода. Иначе `async Task` + `RequiresResponse=true` слал в ответ `{}` / падал на Avro/Protobuf.

**Режимы консюмера:** `ConsumerMode.Single` (1 консюмер/топик, дефолт) и `ConsumerMode.Auto` (несколько консюмеров одной группы на топик, до числа партиций, опционально `MaxConsumersPerTopic`). Auto проверен: 4 консюмера / 4 партиции = **2.15x** throughput на боттлнек-хендлере, exactly-once сохраняется. Shared (1 консюмер на много топиков) — сознательно НЕ делаем.

**Нагрузка/утечки:** leak-тест (30k сообщений, 3 цикла GC) — heap плоский 3.3 MB, 0 роста, потоки стабильны. **Утечки памяти нет.**

**Фиксы из аудита утечек/конкурентности (сделаны):** (1) исключения `Commit`/DLQ-produce обёрнуты — консюмер больше не падает в crash-loop при недоступности DLQ; (2) backoff клампится `MaxRetryDelay` — нет риска eviction по max.poll.interval; (3) consume-цикл на dedicated LongRunning-потоке вместо ThreadPool — нет starvation при N топиках × Auto; (4) `Consumer.Close` идемпотентен — нет гонки двойного close/dispose.

**Тесты: 28, все зелёные** (добавлены leak + Auto-parallelism).

**Гибкость Kafka + SSL (сделано):** принцип «convention over configuration, без lockout». 4 слоя, precedence defaults < типизированные props < `RawConfig` (raw librdkafka key/value) < `Configure*` callback. `KafkaSecurityOptions` (SSL/TLS/SASL) применяется к consumer/producer/**admin**; добавлены `ConfigureAdminClient` + `ConfigureSchemaRegistry` (закрыты дыры). TLS делает сам librdkafka — либа только пробрасывает. Любой librdkafka-ключ доступен. Единственное сознательное ограничение — `EnableAutoCommit` (ломает at-least-once), защищено даже от raw. 7 тестов на precedence/SSL.

**Остаётся (config-DX):**
- `AddAutumnKafka(IConfiguration)` + биндинг appsettings, `IOptions`, ранняя агрегированная валидация, дефолты под минимальный hello-world.
- Producer-only: типизированный `IKafkaProducer.SendAsync<T>(topic, method, payload, key?, correlationId?, messageId?)` + `AddAutumnKafkaProducer(...)` без `GroupId`/скана/консюмеров; снять жёсткое требование `GroupId`, когда консюмеров нет.
- Header-крючки: `correlation-id` (RPC/трейс, сервер эхом), `message-id` (дедуп на стороне юзера) — опциональные, стор не встраиваем.
- Оставшиеся фиксы аудита: transient `IConsumer` резолвится из ROOT-провайдера (handle-retention, усилен Auto-режимом); блокирующий `GetMetadata` sync-over-async; **`CreateTopicAsync` глотает `TopicAlreadyExists` без проверки числа партиций** → Auto молча не масштабируется на pre-existing топике с меньшим числом партиций (warn на старте).
- CI, Samples, README, RPC-клиент (0.2.0, отложен).

**NuGet-пакет (готов к публикации 0.1.0):** csproj с метаданными (tags, description, releaseNotes, projectUrl, repository), Apache-2.0, README упакован как витрина, XML-доки (IntelliSense), SourceLink (GitHub, commit в nuspec), symbols (.snupkg), deterministic build. `dotnet pack -c Release` → `Autumn.Kafka.0.1.0.nupkg` + `.snupkg` собираются чисто. Тесты: **35 зелёных** (unit + 5 Docker).

**Публикация (делает автор — нужен API-ключ, я не пушу):**
```
dotnet pack -c Release -o ./nupkg
dotnet nuget push ./nupkg/Autumn.Kafka.0.1.0.nupkg -k <API_KEY> -s https://api.nuget.org/v3/index.json
dotnet nuget push ./nupkg/Autumn.Kafka.0.1.0.snupkg -k <API_KEY> -s https://api.nuget.org/v3/index.json
git tag v0.1.0 && git push origin v0.1.0
```
2 фикса аудита перед пушем — **сделаны**: (1) `IConsumer` теперь создаётся через фабрику `Func<IConsumer>`, хендлер владеет им и диспозит — DI больше не держит handle до конца процесса (усиливалось Auto); (2) warn на старте, если существующий топик имеет меньше партиций, чем запрошено (Auto молча не масштабируется). Всё под 35 тестами.

**Оценка готовности к 1.0:** ~80%. Ядро + режимы + гибкость + SSL + NuGet-упаковка готовы и под тестами. Остаток (producer-only, appsettings, header-крючки, CI) — в 0.1.x/0.2.0.

---

## Вехи

| Версия | Тема | Блокирует релиз |
|--------|------|-----------------|
| **0.1.0** | Стабилизация ядра | Lifecycle-регрессия, семантика ошибок, тесты валидации |
| **0.2.0** | RPC-клиент | Главный дифференциатор; без него ниша не закрыта |
| **0.3.0** | Наблюдаемость + error envelope | Трейсинг, метрики, типизированные ошибки RPC через провод |
| **0.9.0** | Полировка + Samples + докиs | RC: всё есть, стабилизируем API |
| **1.0.0** | Стабильный публичный релиз | SemVer-гарантии, заморозка контракта |

---

## 0.1.0 — Стабилизация ядра

Цель: то, что уже написано, работает корректно и покрыто тестами. Основа под RPC.

Полностью раскрыто в IMPROVEMENT_PLAN.md. Сжатый чеклист:

- **Lifecycle (критично, регрессия):** `KafkaConsumerHostedService` держит consume-задачу, `ExecuteAsync` её ждёт, `Stop` policy снова останавливает хост, graceful shutdown коммитит оффсеты, хендлер диспозится.
- **Семантика ошибок:** детерминированные ошибки не ретраятся; транзиентные — backoff; `ConsumeException` убивает только на `IsFatal`; DLQ через `Partition.Any` без admin-roundtrip на каждое сообщение; options в конструктор (убрать резолв из горячего пути); запрет `EnableAutoCommit=true`.
- **Тесты валидации:** unit-матрица на `BuildHandlerConfigs` (9 сценариев) + `ServiceResolver` (все формы возврата). Без Kafka.
- **Интеграция (Testcontainers):** JSON roundtrip, poison→DLQ, Stop останавливает хост, graceful shutdown без дублей, рестарт брокера.

**Definition of done:** зелёный CI (build + unit + integration), README отражает ErrorPolicy/DLQ/retry, тег `v0.1.0`.

---

## 0.2.0 — RPC-клиент (главная фича)

Это то, чего нет ни у KafkaFlow, ни у MassTransit в первоклассном виде. Дизайн ниже — результат отдельной проработки под углом «сначала отказы». Он **фиксирует серверный контракт** и требует ровно одно обратносовместимое изменение сервера.

### Архитектурное решение (кратко)

- **Корреляция:** новый header `correlation-id` (GUID), сервер эхом возвращает его в ответе. Плюс legacy-режим `KeyEcho` без изменения сервера (эксплуатирует уже существующее копирование `Key` запроса в ответ).
- **Роутинг ответов:** новый header `reply-to`, один reply-топик **на приложение** (дефолт `{GroupId}.replies`), не на инстанс.
- **Мульти-инстанс:** каждый инстанс вручную `Assign()` все партиции reply-топика с `Offset.End`, без consumer group → **нет ребаланса как класса отказа**. Фильтрация чужих ответов — промах в pending-map по сырым байтам header (без десериализации).
- **Pending-map:** `ConcurrentDictionary<corrId, PendingCall>` с тремя гарантированными путями удаления (ответ / sweeper по дедлайну / отмена вызывающим), жёсткий `MaxTimeout`, семафор `MaxInFlightCalls` как backpressure и потолок памяти.
- **API:** низкоуровневый `IKafkaRpcClient.CallAsync<TReq,TRes>` + типизированные прокси через `DispatchProxy` из интерфейсов `[KafkaClient]`, переиспользующих существующий `[KafkaMethod]`.

### Требуемое изменение сервера (единственное, ~15 строк)

Файл `MessageHandlers/BaseMessageHandler.cs`, блок построения ответа (`HandleMessage`) и `SendResponse`. Полностью обратносовместимо:

1. Если в запросе есть header `correlation-id` → добавить его **без изменений** в headers ответа.
2. Если в запросе есть header `reply-to` → отправить ответ в этот топик с партицией `-1` (`Partition.Any`) через существующий `Producer.ProduceAsync`, минуя `config.ResponseTopicConfig`/`ResponseTopicPartition`; подавить `KafkaConfigurationException` про отсутствие ResponseTopic в атрибуте.
3. Обоих headers нет → байт-в-байт текущее поведение (старые клиенты и атрибутные response-топики не ломаются).

Сохранить: `Key` ответа = `Key` запроса; headers `method`, `sender`, `traceparent`.

### Публичный API (эскиз)

```csharp
public interface IKafkaRpcClient
{
    Task<TResponse?> CallAsync<TRequest, TResponse>(
        string requestTopic, string method, TRequest request,
        KafkaRpcCallOptions? callOptions = null, CancellationToken ct = default);

    Task SendAsync<TRequest>(              // fire-and-forget: без correlation-id/reply-to/pending
        string requestTopic, string method, TRequest request,
        KafkaRpcCallOptions? callOptions = null, CancellationToken ct = default);
}

[AttributeUsage(AttributeTargets.Interface)]
public sealed class KafkaClientAttribute(string requestTopic) : Attribute
{
    public string RequestTopic { get; } = requestTopic;
    public MessageHandlerType HandlerType { get; set; } = MessageHandlerType.JSON;
    public string? ReplyTopic { get; set; }   // required в LegacyCorrelationMode
    public string? SenderName { get; set; }
}

// [KafkaClient("orders-request")]
// public interface IOrderClient {
//     [KafkaMethod("CreateOrder")] Task<OrderResult> CreateOrderAsync(CreateOrderRequest r, CancellationToken ct = default);
//     [KafkaMethod("DeleteOrder", RequiresResponse = false)] Task DeleteOrderAsync(DeleteOrderCommand c, CancellationToken ct = default);
// }
// builder.Services.AddAutumnKafkaRpcClient();   // после AddAutumnKafka(...)
```

Опции: `ReplyTopic`, `ReplyTopicPartitions=3`, `ReplyTopicRetention=10min` (retention.ms — авто-GC осиротевших ответов), `DefaultTimeout=30s`, `MaxTimeout=5min` (жёсткий потолок, `Infinite` запрещён → map доказуемо дренируется), `SendTimeout=10s` (message.timeout.ms, отдельно от RPC-таймаута), `MaxInFlightCalls=1024`, `SweepInterval=250ms`, `LegacyCorrelation=None`.

### Типизированные ошибки

Каждый вызов завершается ровно одним из шести исходов — никогда не висит вечно:

| Исключение | Смысл | Retry-семантика для вызывающего |
|-----------|-------|--------------------------------|
| (результат) | ответ получен и десериализован | — |
| `KafkaRpcTimeoutException` | дедлайн истёк | **неизвестный исход** — сервер мог выполнить (at-least-once). Идемпотентность или status-query |
| `KafkaRpcSendException` | produce не удался | сервер **не** получил (idempotent producer + acks=all) — безопасно ретраить |
| `KafkaRpcResponseException` | ответ пришёл, десериализация упала | не ретраить вслепую |
| `OperationCanceledException` | отмена вызывающим | — |
| `KafkaRpcClientClosedException` | клиент/хост останавливается | ретрай после рестарта |

### Обработка отказов (ключевое из дизайна)

- **Timeout** — единый sweeper (250ms, один на reply-консюмер, без таймера на вызов). Поздний ответ → промах → сирота, тихо отброшен.
- **Краш инстанса между send и reply** — pending-map умирает с процессом; ответ становится сиротой для всех (свежие GUID + `Offset.End` при рестарте) → GC по retention. Ни janitor, ни координации, ни утечки.
- **Ребаланс reply-консюмера** — удалён как класс: ручной `Assign()` не входит в group protocol. Рост партиций — таймер `PartitionMetadataRefresh`.
- **Дубли ответов** (сервер коммитит после produce → краш = redelivery + второй ответ) — `TryRemove` атомарный single-winner gate, дубль промахивается, `TrySet*` вторая защита.
- **Утечка pending-map** — невозможна по построению: конечный дедлайн у каждой записи + sweeper-backstop + семафор-потолок + drain при Dispose.
- **Медленный вызывающий не стопорит диспатч** — `TaskCreationOptions.RunContinuationsAsynchronously`.
- **Брокер лёг** — send-фаза: fail-fast `KafkaRpcSendException`; await-фаза: reply-консюмер переживает реконнект librdkafka (транзиент/фатал split как на сервере).
- **Битый ответ** — десериализация только после матча, обёрнута per-message → падает ровно один вызов, консюмер жив.

### План внедрения 0.2.0

1. **Извлечь serde** — вынести `Serialize/DeserializeAsync` из `BaseMessageHandler` в internal `IPayloadSerde` (Json/Avro/Protobuf), общий для сервера и клиента. Гарантирует байт-идентичный формат провода. Серверные хендлеры делегируют, публичная поверхность не меняется.
2. **Серверное эхо** — 15-строчное изменение выше + тест обратной совместимости (старый клиент → старое поведение).
3. **Reply-консюмер** — `KafkaRpcReplyConsumerService : BackgroundService`: ensure topic (retention.ms), `Assign(all @ End)`, сигнал `_ready`; consume-loop + sweeper-loop; никаких коммитов; `group.id = {GroupId}-rpc-{Guid}` (только для валидации конфига librdkafka).
4. **`IKafkaRpcClient`** — pending-map, семафор, produce с `EnableIdempotence/Acks=All`, gating на `_ready`.
5. **Типизированные прокси** — `DispatchProxy` из `[KafkaClient]`; `RequiresResponse=false` → `SendAsync`.
6. **DI** — `AddAutumnKafkaRpcClient` + overload для чистого клиента без `[KafkaService]`.
7. **Тесты** — happy path; timeout; N-инстансов (сирота-фильтр); краш; дубль; битый ответ; legacy KeyEcho против неизменённого сервера.

**Definition of done:** RPC roundtrip end-to-end на Testcontainers, все шесть путей завершения покрыты, README с RPC-разделом и таблицей ошибок, тег `v0.2.0`.

---

## 0.3.0 — Наблюдаемость + error envelope

- **Трейсинг:** `ActivitySource("Autumn.Kafka")` — activity на обработку и на produce; проброс `traceparent` (частично есть). Сквозной trace producer→consumer→response через OpenTelemetry без обязательных зависимостей.
- **Метрики:** `Meter("Autumn.Kafka")` — `messages_processed/failed/dlq`, `retry_attempts`, `processing_duration`; для RPC — `rpc_calls/timeouts/orphans`, `rpc_call_duration`, gauge `rpc_reply_lag`. Опциональный `IHealthCheck`.
- **Error envelope (открытый вопрос дизайна):** сервер ловит исключения хендлера для `RequiresResponse`-методов и публикует error-ответ (headers `rpc-status`/`error-type` + тело), эхом corrId → клиент падает быстро с реальной ошибкой вместо полного таймаута. Второе изменение сервера, нетривиально взаимодействует с retry/ErrorPolicy/DLQ — потому отдельной вехой после того, как corrId-эхо устоялось.

**Definition of done:** trace виден при подключённом OTel SDK; метрики экспортируются; error envelope спроектирован и покрыт тестом; тег `v0.3.0`.

---

## 0.9.0 — RC: полировка, Samples, доки

- **Samples/** проект: (a) fire-and-forget consumer, (b) request/reply сервер+клиент, (c) чистый клиент из ASP.NET.
- **Пробелы DX** (закрыть перед заморозкой API): сканирование нескольких сборок; биндинг опций через `IOptions`/appsettings; доступ к `Key`/`Headers`/timestamp внутри хендлера; публичный типизированный producer для fire-and-forget (сейчас `KafkaProducer` заточен под ответы); passthrough SASL/SSL и auth Schema Registry; несколько consumer group в процессе; хендлеры на internal-классах. *(Полный аудит DX отложен — см. «Долг проработки».)*
- **Docs:** полный README (таблица всех опций, идемпотентность, безопасность), CHANGELOG, XML-доки на всё публичное, DocFX/страница API.
- Заморозка публичного API: пометить internal всё, что не контракт.

**Definition of done:** три сэмпла запускаются; API-обзор пройден; тег `v0.9.0` как RC.

---

## 1.0.0 — Стабильный релиз

- SemVer-гарантии, публичный контракт заморожен.
- `Directory.Build.props`: `TreatWarningsAsErrors`, `AnalysisLevel=latest`.
- GitHub Actions: build+test на PR/main; pack+push в NuGet по тегу `v*`.
- Badges CI/NuGet/coverage. LICENSE, SECURITY.md.

**Definition of done:** NuGet `1.0.0` опубликован, CI зелёный, доки полные.

---

## Конкурентный скоуп 1.0

*(из предыдущего анализа; полный аудит vs KafkaFlow/MassTransit/Wolverine отложен — см. «Долг проработки»)*

- **Table-stakes для любого 1.0:** тесты + CI (0.1.0), наблюдаемость (0.3.0), Samples + доки (0.9.0), SASL/SSL passthrough (0.9.0).
- **Дифференциатор — удваиваем:** первоклассный typed request/reply (0.2.0). Это единственное предложение, которого нет у конкурентов в .NET. Всё внимание сюда.
- **Явные НЕ-цели (отказываемся, чтобы остаться лёгкими):** саги/оркестрация, generic message bus поверх нескольких транспортов, встроенный outbox, визуальный dashboard, кастомные протоколы сериализации сверх Json/Avro/Protobuf. Кто это хочет — берёт MassTransit. Наша ценность в узости.

---

## Порядок и зависимости

```
0.1.0 (ядро+тесты) ──▶ 0.2.0 (RPC) ──▶ 0.3.0 (obs+envelope) ──▶ 0.9.0 (RC) ──▶ 1.0.0
                          │
   IPayloadSerde extract ─┘ (первый шаг 0.2.0, разблокирует общий формат провода)
```

Критический путь: **0.1.0 lifecycle-фикс → тесты → извлечение serde → RPC**. Не начинать RPC, пока ядро не под тестами — RPC переиспользует serde и producer, баги в них всплывут дважды.

---

## Долг проработки (доделать после сброса лимита, 20:00 МСК)

Workflow-панель из 6 агентов упала на session limit; выжил только RPC-дизайн (он в основе 0.2.0). Не хватает — прогнать после сброса:

1. **Аудит plan-vs-code** — адверсариальная проверка IMPROVEMENT_PLAN против кода + баги, которые план пропустил (concurrency, commit-семантика, DI-lifetimes, reflection edge cases).
2. **Аудит DX** — сквозной путь трёх типов адоптеров, полный список пробелов API с приоритетом (черновик в 0.9.0).
3. **Конкурентный скоуп** — детальное сравнение фич vs KafkaFlow/MassTransit/Wolverine, уточнить table-stakes/НЕ-цели.
4. **Судейство RPC** — дизайн-контест (simplicity/failure-modes/dx лучи) не досудил; failure-modes-дизайн принят по умолчанию как единственный дошедший. Стоит прогнать simplicity/dx лучи и свести — возможны graft'ы по эргономике API.
