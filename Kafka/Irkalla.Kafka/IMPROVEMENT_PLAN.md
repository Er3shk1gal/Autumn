# Irkalla.Kafka — план улучшений

> Статус кодовой базы на момент составления: ErrorPolicy/DLQ/retry реализованы, fail-fast валидация конфигурации на старте есть, роутинг через `Dictionary`, `AutoCreateTopics` работает, мёртвый код вычищен. Сборка чистая (0 warnings).
>
> **Вне скоупа этого плана:** RPC-клиент (`IKafkaRpcClient`, correlation-id, pending-map, таймауты). Реализуем отдельным этапом после завершения этого плана.

---

## Фаза 1 — Lifecycle консюмера (критично, регрессия)

### 1.1. Починить fire-and-forget в `KafkaConsumerHostedService`

**Файл:** `Hosting/KafkaConsumerHostedService.cs` (строки 57–73)

**Проблема.** Consume-цикл запускается так:

```csharp
_ = Task.Factory.StartNew(async () => { ... }, TaskCreationOptions.LongRunning);
```

Три дефекта:

1. **`ErrorPolicy.Stop` не останавливает приложение.** `Stop` → `throw lastException` → исключение ловится catch-блоком внутри fire-and-forget лямбды → `LogError` → всё. Консюмер мёртв, приложение живёт, health-check зелёный, топик не читается. Zombie-состояние. То же самое с инфраструктурным `ConsumeException` (rethrow из `BaseMessageHandler.Consume`).
2. **Graceful shutdown сломан.** `ExecuteAsync` завершается сразу после запуска задачи. При остановке хоста никто не ждёт consume-цикл: `Consumer.Close()` (финальный коммит оффсетов, выход из группы) гонится с завершением процесса.
3. **`StartNew(async ...)` возвращает `Task<Task>`.** Выделенный LongRunning-поток живёт только до первого `await`, дальше продолжения уезжают на thread pool. Паттерн не делает того, что задумано.

**Решение.**

```csharp
public class KafkaConsumerHostedService : BackgroundService
{
    private BaseMessageHandler? _handler;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ... создание топика / проверка существования (без изменений) ...

        _handler = CreateHandler();

        // Уводим блокирующий цикл с потока запуска хоста,
        // но НЕ теряем задачу — ExecuteAsync ждёт её до конца.
        await Task.Run(() => _handler.Consume(stoppingToken), stoppingToken);
    }

    public override void Dispose()
    {
        _handler?.Dispose();
        base.Dispose();
    }
}
```

Ключевые моменты:

- `await Task.Run(...)` — исключение из `Consume` пролетает наверх через `ExecuteAsync`. Дальше срабатывает штатный механизм .NET: `HostOptions.BackgroundServiceExceptionBehavior` (дефолт — `StopHost`). `ErrorPolicy.Stop` снова означает «стоп».
- `OperationCanceledException` при штатной остановке НЕ логировать как ошибку (BackgroundService сам его корректно обрабатывает).
- `StopAsync` базового класса дождётся `ExecuteAsync` в пределах `HostOptions.ShutdownTimeout` — `Consumer.Close()` успевает выполниться.
- `Dispose` хостед-сервиса диспозит хендлер (сейчас `BaseMessageHandler.Dispose` не вызывается никем).

**Критерии приёмки:**
- [ ] `ErrorPolicy.Stop` + poison message → хост останавливается (интеграционный тест).
- [ ] SIGTERM во время обработки → `Consumer.Close()` выполнен, оффсеты закоммичены, лог «Consumer closed».
- [ ] Штатная остановка не пишет ERROR в лог.

---

## Фаза 2 — Семантика ошибок и retry

### 2.1. Не ретраить детерминированные ошибки

**Файл:** `MessageHandlers/BaseMessageHandler.cs` (retry-цикл, строки 48–68)

**Проблема.** Retry-цикл повторяет ВСЕ ошибки `MaxRetries` раз без задержки:

- Отсутствие header `method`, неизвестный метод, ошибка десериализации — детерминированные ошибки. Повтор бессмыслен: 4 одинаковых падения подряд, горячий спин.
- Бизнес-исключение после частичного side effect → 3 мгновенных повтора = до 4 дублей операции у неидемпотентного хендлера.

**Решение.** Классифицировать:

| Класс ошибки | Примеры | Поведение |
|---|---|---|
| Детерминированная (poison) | `KafkaConsumerException` (нет header, неизвестный метод), `KafkaConfigurationException`, ошибки десериализации | Сразу применить `ErrorPolicy`, **без retry** |
| Потенциально транзиентная | Всё остальное (бизнес-исключения из хендлера, таймауты внешних вызовов) | Retry с backoff, затем `ErrorPolicy` |

Ошибки десериализации сейчас прилетают «сырыми» (`JsonException`, исключения Avro/Protobuf serdes) — обернуть в `KafkaConsumerException` внутри `InvokeServiceMethodAsync`, чтобы классификация работала по типу.

```csharp
catch (Exception ex) when (ex is KafkaConsumerException or KafkaConfigurationException)
{
    lastException = ex;
    break; // детерминированная — сразу к ErrorPolicy
}
catch (Exception ex)
{
    lastException = ex;
    attempts++;
    if (attempts <= options.MaxRetries)
    {
        var delay = TimeSpan.FromMilliseconds(
            options.RetryDelay.TotalMilliseconds * Math.Pow(2, attempts - 1));
        await Task.Delay(delay, cancellationToken);
    }
}
```

**Новая опция** в `IrkallaKafkaOptions`:

```csharp
/// <summary>
/// Base delay between retries of a failed message. Grows exponentially
/// (delay * 2^attempt). Default: 1 second.
/// </summary>
public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
```

**Документация:** в README явно указать — при `MaxRetries > 0` хендлеры должны быть идемпотентными.

**Критерии приёмки:**
- [ ] Сообщение без header `method` → ровно 1 попытка, затем policy.
- [ ] Бизнес-исключение → `MaxRetries` повторов с растущей задержкой.
- [ ] Между повторами есть `Task.Delay`, отменяемый по `cancellationToken`.

### 2.2. `ConsumeException`: убивать консюмер только на fatal

**Файл:** `MessageHandlers/BaseMessageHandler.cs` (строки 103–109)

**Проблема.** Любой `ConsumeException` → `throw` → консюмер умирает. Confluent кидает `ConsumeException` и на транзиентных ошибках (рестарт брокера, временная недоступность партиции). Перезапуск брокера не должен класть сервис.

**Решение.**

```csharp
catch (ConsumeException ex) when (!ex.Error.IsFatal)
{
    Logger.LogError(ex, "Transient Kafka consume error on topic '{Topic}', continuing", ...);
    // цикл продолжается — librdkafka сам восстанавливает соединение
}
catch (ConsumeException ex)
{
    Logger.LogCritical(ex, "Fatal Kafka error on topic '{Topic}', stopping consumer", ...);
    throw;
}
```

**Критерии приёмки:**
- [ ] Рестарт брокера в интеграционном тесте → консюмер продолжает работу после восстановления.
- [ ] `ex.Error.IsFatal == true` → исключение уходит наверх → хост останавливается (после 1.1).

### 2.3. `KafkaProducer`: партиция `-1` = `Partition.Any`, кэш для DLQ

**Файлы:** `Utils/KafkaProducer.cs`, `MessageHandlers/BaseMessageHandler.cs` (`SendToDlq`, строка 254)

**Проблема.** DLQ шлётся через `ProduceAsync(topicConfig, -1, message)`:

- `EnsureTopicAvailableAsync` → `CheckTopicContainsPartitions(topic, -1)` → партиции с id `-1` не бывает → всегда `false` → **каждое** poison-сообщение выполняет `GetMetadata` (таймаут до 10 с) + `CreateTopicsAsync` (ловится `TopicAlreadyExists`). Два admin-roundtrip на каждое сообщение в DLQ.
- Значение `-1` совпадает с `Partition.Any`, поэтому сам produce работает — но случайно, семантика нигде не выражена.

**Решение.**

```csharp
public async Task<bool> ProduceAsync(TopicConfig topic, int partition, Message<string, byte[]> message)
{
    await EnsureTopicAvailableAsync(topic, partition);

    var target = partition < 0
        ? new TopicPartition(topic.TopicName, Partition.Any)
        : new TopicPartition(topic.TopicName, new Partition(partition));
    // ...
}

private async Task EnsureTopicAvailableAsync(TopicConfig topic, int partition)
{
    // partition < 0 → достаточно существования топика (проверка кэшируется)
    var ok = partition < 0
        ? kafkaTopicManager.CheckTopicExists(topic.TopicName)
        : kafkaTopicManager.CheckTopicContainsPartitions(topic.TopicName, partition);

    if (ok) return;

    await kafkaTopicManager.CreateTopicAsync(topic.TopicName, topic.PartitionsCount, topic.ReplicationFactor);
}
```

Дополнительно в `KafkaTopicManager.CreateTopicAsync`: после успешного создания (или `TopicAlreadyExists`) класть топик в `_verifiedTopics` — иначе кэш не прогревается через путь создания.

**Критерии приёмки:**
- [ ] Второе poison-сообщение подряд → ноль admin-запросов (проверяется моком `IAdminClient`).
- [ ] DLQ-топик создаётся один раз, дальше только produce.

### 2.4. Options в конструктор, убрать резолв из горячего пути

**Файлы:** `MessageHandlers/BaseMessageHandler.cs` (строки 37, 144), `MessageHandlers/JsonMessageHandler.cs` (строки 23, 29), `Hosting/KafkaConsumerHostedService.cs` (`CreateHandler`)

**Проблема.** `IrkallaKafkaOptions` — синглтон, но резолвится:
- в **каждой итерации** consume-цикла (`BaseMessageHandler.Consume`, строка 37);
- ещё раз на каждое сообщение с ответом (`HandleMessage`, строка 144);
- **дважды на каждое сообщение** в JSON serialize/deserialize — причём через полное имя `Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<...>` вместо `using`.

**Решение.** Добавить `IrkallaKafkaOptions options` в primary constructor `BaseMessageHandler`, прокинуть из `CreateHandler`. Все `GetRequiredService<IrkallaKafkaOptions>()` внутри хендлеров удалить. В `JsonMessageHandler` — обычный `using Microsoft.Extensions.DependencyInjection;` больше не нужен вовсе.

**Критерии приёмки:**
- [ ] `grep GetRequiredService<IrkallaKafkaOptions>` по `MessageHandlers/` — пусто.

### 2.5. Запретить `EnableAutoCommit = true`

**Файл:** `Extensions/ServiceCollectionExtensions.cs` (`AddIrkallaKafka`), `Configuration/IrkallaKafkaOptions.cs`

**Проблема.** Вся модель доставки построена на ручном `Commit` после успешной обработки (или после DLQ). `EnableAutoCommit = true` ломает её молча: оффсеты коммитятся фоном ДО обработки → потеря сообщений при падении, а ручные коммиты продолжают выполняться параллельно.

**Решение.** Пока честная поддержка auto-commit не спроектирована — fail fast:

```csharp
if (options.EnableAutoCommit)
{
    throw new KafkaConfigurationException(
        "EnableAutoCommit is not supported: Irkalla.Kafka commits offsets manually " +
        "after successful processing (or after DLQ publish) to guarantee at-least-once delivery.");
}
```

Либо удалить опцию целиком (сейчас 0.0.x, ломать нечего). Предпочтительно — удалить.

**Критерии приёмки:**
- [ ] Опция удалена ИЛИ `AddIrkallaKafka` бросает исключение при `true`.

### 2.6. Мелочь одной пачкой

- **`throw lastException!`** (`BaseMessageHandler.cs:95`) затирает оригинальный stack trace → `ExceptionDispatchInfo.Capture(lastException).Throw()`.
- **`success=false` без исключения**: если `HandleMessage` вернёт `false` без throw, `lastException == null` → `throw null!` → NRE. Сейчас путь недостижим, но защититься: `lastException ?? new KafkaConsumerException("Handler returned false")`.
- **`ServiceResolver.GetKafkaServiceTypes`** — не используется, удалить.
- **`KafkaTopicManager.CheckTopicSatisfiesRequirements` / `DeleteTopicAsync`** — не используются библиотекой; либо удалить, либо оставить осознанно как публичное API (тогда покрыть тестами).
- **`ResolveService`**: fallback «первый попавшийся интерфейс» (`GetInterfaces().FirstOrDefault()`) — недетерминизм при нескольких интерфейсах. Резолвить по конкретному типу; fallback по интерфейсу — только если интерфейс ровно один, иначе понятное исключение.

---

## Фаза 3 — Наблюдаемость и производительность

### 3.1. Вернуть трейсинг

Пакет `Confluent.Kafka.Extensions.Diagnostics` был удалён как неиспользуемый — правильно. Но наблюдаемость нужна. Минимум без зависимостей:

- `ActivitySource("Irkalla.Kafka")`: activity на обработку сообщения (`autumn.kafka.consume`) с тегами `messaging.system=kafka`, `messaging.destination`, `method`, и на produce ответа/DLQ.
- Прокидывать `traceparent` header: читать из входящего сообщения → `Activity.SetParentId`, писать в исходящее (ответ, DLQ). Даёт сквозной distributed trace через OpenTelemetry без обязательных зависимостей.

**Критерии приёмки:**
- [ ] При подключённом OpenTelemetry SDK видна цепочка producer → consumer → response.

### 3.2. Метрики здоровья консюмера

- Счётчики через `System.Diagnostics.Metrics.Meter("Irkalla.Kafka")`: `messages_processed`, `messages_failed`, `messages_dlq`, `retry_attempts`, histogram `processing_duration`.
- Опционально: `IHealthCheck`, сообщающий, жив ли consume-цикл каждого топика (после 1.1 упавший цикл валит хост, но для `ErrorPolicy.Skip/Dlq` health-check всё равно полезен).

### 3.3. Параллелизм обработки (спроектировать, потом решить)

Сейчас: один топик = один поток, обработка строго последовательная. Для 1 партиции это корректный максимум. Для N партиций можно обрабатывать партиции параллельно, не ломая порядок внутри партиции. Вариант: `MaxDegreeOfParallelism` (дефолт 1), channel-per-partition, коммит по нижней границе обработанных оффсетов партиции.

**Не делать сразу** — усложняет коммит-логику; сначала тесты и стабильность. Зафиксировано, чтобы не потерять.

---

## Фаза 4 — Тесты, CI, релиз

### 4.1. Unit-тесты (без Kafka)

Проект `Irkalla.Kafka.Tests` (xUnit).

`MessageHandlerFactory.BuildHandlerConfigs` — теперь чистая функция, идеальна для матрицы:

| Сценарий | Ожидание |
|---|---|
| `[KafkaService]` без `[KafkaMethod]` | `KafkaConfigurationException` |
| Дубль `MethodName` на одном топике | `KafkaConfigurationException` |
| Конфликт `HandlerType` на общем топике | `KafkaConfigurationException` |
| `RequiresResponse=true` без `ResponseTopic` | `KafkaConfigurationException` |
| Два payload-параметра | `KafkaConfigurationException` |
| PROTOBUF + параметр без `IMessage<T>` | `KafkaConfigurationException` |
| Валидный сервис: 2 метода, ответ, партиции | корректный `MessageHandlerConfig` |
| Два сервиса на одном топике | один конфиг, методы объединены |
| Метод с `CancellationToken` | payload распознан верно |

`ServiceResolver.InvokeMethodAsync`: sync/`Task`/`Task<T>`/`ValueTask`/`ValueTask<T>`/`void`, `VoidResultMarker`, unwrap `TargetInvocationException`, резолв по типу и по интерфейсу.

Retry-логика (после 2.1): детерминированная ошибка — 1 попытка; транзиентная — N попыток с задержкой; порядок «retry → policy».

### 4.2. Интеграционные тесты (Testcontainers)

Пакет `Testcontainers.Kafka`:

1. **JSON roundtrip**: produce запроса с header `method` → хендлер вызван → ответ в response-топике с headers `method`/`sender`.
2. **Poison → DLQ**: битый payload → сообщение в `<topic>.dlq` с headers `error`/`stacktrace`, оффсет закоммичен, следующее сообщение обработано.
3. **`ErrorPolicy.Stop`**: poison → хост останавливается (проверка 1.1).
4. **Graceful shutdown**: остановка хоста во время обработки → оффсет обработанного закоммичен, дублей после рестарта нет.
5. **Рестарт брокера**: пауза контейнера → консюмер переживает и продолжает (проверка 2.2).

### 4.3. CI и релиз

- GitHub Actions: `dotnet build` + `dotnet test` на PR и main; `dotnet pack` + push в NuGet по тегу `v*`.
- `Directory.Build.props`: `TreatWarningsAsErrors`, `AnalysisLevel=latest`.
- README обновить: ErrorPolicy/DLQ/retry (+требование идемпотентности), `JsonSerializerOptions`, `AutoCreateTopics`, `RetryDelay`, таблица всех опций, badges CI/NuGet.
- CHANGELOG.md, версия `0.1.0`.

---

## Порядок выполнения

```
1.1  ── критично, регрессия lifecycle          ← начать здесь
2.1─2.6 ── один PR «error semantics», меняют публичное поведение — в один релиз
4.1  ── unit-тесты (можно параллельно с фазой 2, валидация уже стабильна)
4.2  ── интеграционные тесты (после 1.1 и 2.x — тестируют именно их)
3.1─3.2 ── наблюдаемость
4.3  ── CI + README + 0.1.0
3.3  ── параллелизм — отдельное проектирование, после 0.1.0
```

## Явно отложено (не забыть)

- **RPC-клиент**: correlation-id header, `IKafkaRpcClient.CallAsync<TReq, TRes>(method, request, timeout)`, pending-map (`ConcurrentDictionary<string, TaskCompletionSource>`), фоновый консюмер response-топика, таймауты/отмена. Стратегический дифференциатор библиотеки — реализуем после стабилизации ядра (фазы 1–2 + тесты). Correlation-id header можно заложить раньше в рамках 3.1 (тот же механизм, что traceparent).
- Параллелизм по партициям (3.3).
- Честная поддержка auto-commit (если вообще нужна).
