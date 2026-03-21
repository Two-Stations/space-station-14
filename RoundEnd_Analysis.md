# Анализ системы RoundEnd и EmergencyShuttle

Данный документ предоставляет комплексный анализ проблемы с отзывом эвакуационного шаттла. Коренная причина — не ошибка в существующей логике, а полное отсутствие механизма отзыва, усугубленное архитектурными проблемами (глобальное состояние, циклические зависимости), которые препятствуют надежной работе в многостанционном режиме. В документе предложено как немедленное исправление, так и долгосрочная стратегия рефакторинга.

---

## 1. Обзор систем

Система завершения раунда в Space Station 14 включает несколько механизмов. Данный анализ фокусируется на проблеме отзыва эвакуационного шаттла, но для полноты картины важно понимать все способы завершения раунда.

### 1.1. Завершение через эвакуацию

Этот способ является основным нарративным методом завершения раунда и включает две ключевые системы:

- **`RoundEndSystem`**: Высокоуровневая система, которая управляет логикой и таймерами для *запроса* эвакуации. Она запускает обратный отсчет, по истечении которого должен быть вызван шаттл.
- **`EmergencyShuttleSystem`**: Система, которая управляет состоянием и поведением эвакуационного шаттла (создание, вызов, стыковка, возвращение на ЦентКом).

Команда администратора `callevac` взаимодействует с `RoundEndSystem`, чтобы начать процесс эвакуации.

### 1.2. Прямое завершение раунда: `EndRoundCommand`

Существует также второй, более прямой способ завершения раунда.

- **Файл**: `Content.Server/GameTicking/Commands/EndRoundCommand.cs`
- **Команда**: `endround`

Эта административная команда предоставляет прямой и немедленный способ закончить раунд.

**Поведение:**
1. Команда проверяет, что раунд в данный момент активен (`RunLevel == InRound`).
2. Она напрямую вызывает метод `_gameTicker.EndRound()`.

В отличие от `callevac`, эта команда **не вызывает эвакуационный шаттл** и не запускает никаких таймеров обратного отсчета в `RoundEndSystem`. Она немедленно переводит игру в состояние `PostRound`, завершая все игровые процессы. Это "жесткий" способ, который следует отличать от "мягкого" нарративного завершения через эвакуацию.

---

## 2. Проблема: невозможность повторного вызова шаттла после отзыва

Пользователь сообщил, что после вызова шаттла и его последующего отзыва, повторно вызвать шаттл становится невозможно. Анализ кода подтвердил, что такая проблема вызвана не ошибкой в логике отзыва, а **полным отсутствием самой механики отзыва шаттла**.

### 2.1. Поток, приводящий к ошибке

1.  **Вызов**: Администратор запускает `callevac`. `RoundEndSystem` начинает обратный отсчет.
2.  **Диспетчеризация**: По истечении таймера (`countdownTime`) в `RoundEndSystem`, вызывается метод `_shuttle.CallEmergencyShuttle()`.
3.  **`EmergencyShuttleSystem.CallEmergencyShuttle()`**:
    - Система проверяет, что шаттл еще не был вызван для данной станции, обращаясь к внутреннему списку `_calledStations`.
    - **ID станции добавляется в `_calledStations`**. С этого момента `EmergencyShuttleSystem` "помнит", что для этой станции шаттл уже был вызван.
    - Шаттл отправляется к станции.
4.  **Попытка отзыва**: Администратор использует команду отзыва (например, `recallevac`), которая, как правило, вызывает `RoundEndSystem.CancelRoundEndCountdown()`.
    - Эта функция была предназначена только для **отмены изначального таймера**, *до* того, как шаттл будет отправлен. Она **не содержит логики для возврата уже отправленного шаттла**.
    - В результате, состояние в `EmergencyShuttleSystem` не меняется. `_calledStations` все еще содержит ID станции.
5.  **Повторный вызов**: Администратор снова использует `callevac`.
    - `RoundEndSystem` снова запускает таймер.
    - По истечении таймера вновь вызывается `_shuttle.CallEmergencyShuttle()`.
    - Внутри `CallEmergencyShuttle` происходит проверка: `if (_calledStations.Contains(station.Value)) return;`.
    - Поскольку ID станции **никогда не удалялся из списка `_calledStations`**, проверка проваливается, и функция немедленно завершается.
    - **Результат**: Шаттл не может быть вызван повторно до конца раунда.

## 3. Причина: Отсутствие механизма отзыва

Корень проблемы — не рассинхронизация, а отсутствие как таковой функции отзыва в `EmergencyShuttleSystem`. `_calledStations` очищается только при перезапуске раунда, что делает вызов шаттла одноразовым действием.

## 4. Предлагаемое решение и анализ влияния

Для устранения проблемы необходимо реализовать полноценный механизм отзыва шаттла, который будет корректно сбрасывать состояние в `EmergencyShuttleSystem` и возвращать шаттл на исходную позицию.

### 4.1. План реализации

#### Шаг 1: Создать метод `EmergencyShuttleSystem.RecallShuttle()`

В `EmergencyShuttleSystem.cs` необходимо добавить новый публичный метод, который будет обрабатывать отзыв.

```csharp
// В EmergencyShuttleSystem.cs

public void RecallShuttle(EntityUid? station = null)
{
    var stationsToRecall = new List<EntityUid>();

    if (station != null)
    {
        if (_calledStations.Contains(station.Value))
            stationsToRecall.Add(station.Value);
    }
    else
    {
        stationsToRecall.AddRange(_calledStations);
    }

    if (stationsToRecall.Count == 0)
        return;

    foreach (var stationUid in stationsToRecall)
    {
        if (!TryComp<StationEmergencyShuttleComponent>(stationUid, out var stationComp) || !stationComp.Called)
            continue;
        
        _calledStations.Remove(stationUid);
        stationComp.Called = false;

        var shuttleEntity = stationComp.EmergencyShuttle;
        if (shuttleEntity == null || !TryComp<ShuttleComponent>(shuttleEntity, out var shuttleComp))
            continue;

        // Безопасный возврат шаттла.
        // Прямого способа отменить FTL-прыжок в ShuttleSystem нет.
        if (TryComp<FTLComponent>(shuttleEntity, out var ftlComp))
        {
            // ЭТО МОЖЕТ ПРИВЕСТИ К ТОМУ, ЧТО ШАТТЛ СНАЧАЛА ПРИЛЕТИТ НА СТАНЦИЮ, А ПОТОМ СРАЗУ УЛЕТИТ.
            // Для более гладкого поведения потребовалась бы полноценная реализация механизма отмены FTL-прыжка,
            // как описано в разделе 5.2.
            _dock.Undock(shuttleEntity.Value, shuttleComp);
        }

        // Возвращаем шаттл на ЦентКом
        if (TryComp<StationCentcommComponent>(stationUid, out var centcommComp) && centcommComp.MapEntity.HasValue)
        {
            var homeMap = centcommComp.MapEntity.Value;
            var homeCoords = new EntityCoordinates(homeMap, _random.NextVector2(100f));
            _shuttle.FTLToCoordinates(shuttleEntity.Value, shuttleComp, homeCoords, Angle.Zero, startupTime: 0.1f);
        }
    }
    
    CleanupEmergencyConsole();
    _commsConsole.UpdateCommsConsoleInterface();
    UpdateAllEmergencyConsoles();
}
```

#### Шаг 2: Модифицировать `RoundEndSystem.CancelRoundEndCountdown()`

В `RoundEndSystem.cs` нужно изменить `CancelRoundEndCountdown`, чтобы он вызывал новую функцию отзыва, если шаттл уже был вызван.

```csharp
// В RoundEndSystem.cs

public void CancelRoundEndCountdown(EntityUid? requester = null, bool forceRecall = false, EntityUid? station = null)
{
    if (_gameTicker.RunLevel != GameRunLevel.InRound)
        return;

    // >>> НАЧАЛО ИЗМЕНЕНИЙ <<<
    if (_shuttle.IsAnyShuttleCalled())
    {
        _shuttle.RecallShuttle(station);
    }
    // >>> КОНЕЦ ИЗМЕНЕНИЙ <<<

    if (_countdownTokenSource == null)
        return;

    if (!forceRecall && (CantRecall || _cooldownTokenSource != null))
        return;
    
    _countdownTokenSource.Cancel();
    _countdownTokenSource = null;

    var logMessage = requester != null 
        ? $"Shuttle recalled by {ToPrettyString(requester.Value):user}" 
        : "Shuttle recalled";
    _adminLogger.Add(LogType.ShuttleRecalled, LogImpact.High, logMessage);

    _chatSystem.DispatchGlobalAnnouncement(Loc.GetString("round-end-system-shuttle-recalled-announcement"),
        Loc.GetString("round-end-system-shuttle-sender-announcement"), false, colorOverride: Color.Gold);
    
    // ... остальная логика ...
}
```

### 4.2. Анализ влияния на другие системы

Это решение не только исправляет основную проблему, но и улучшает общую целостность системы.

-   **Безопасность FTL и отстыковки**: Использование `_dock.Undock` и `FTLToCoordinates` обеспечивает корректную обработку состояний.
-   **Предотвращение ошибок**: Отказ от прямого манипулирования `FTLComponent` предотвращает поломку шаттла.
-   **Состояние консолей**: Явный вызов функций обновления UI гарантирует, что игроки увидят корректный статус.
-   **Системы целей**: `IsAnyShuttleCalled()` будет возвращать `false` после отзыва, предотвращая неверное срабатывание целей.
-   **Гибкость администрирования**: Реализация `RecallShuttle(station)` дает администраторам контроль в сценариях с несколькими станциями.

---

## 5. Долгосрочные улучшения и архитектурные проблемы

Помимо исправления бага, анализ выявил более глубокие архитектурные проблемы.

### 5.1. Циклическая зависимость (Circular Dependency)

В коде существует циклическая зависимость между `RoundEndSystem` и `EmergencyShuttleSystem`.

-   `RoundEndSystem` зависит от `EmergencyShuttleSystem` для вызова шаттла.
-   `EmergencyShuttleSystem` зависит от `RoundEndSystem` для получения данных.

**Почему это проблема?**
-   **Тесная связь**: Системы сложно изменять и тестировать по отдельности.
-   **Трудность рефакторинга**: Изменения в одной системе требуют проверки и правок в другой.

**Как исправить?**
> **Рекомендация:** Разорвать зависимость `EmergencyShuttleSystem` -> `RoundEndSystem`. Вместо вызова методов `_roundEnd.GetCentcomm()`, система `EmergencyShuttleSystem` должна получать эти данные самостоятельно. Например:
> *   **Информацию о ЦентКоме:** `EmergencyShuttleSystem` может найти сущность с компонентом `StationCentcommComponent` при помощи `IEntityManager`.
> *   **Информацию о станции:** Зависимость от `StationSystem` уже существует, и ее можно использовать для получения необходимых данных о станции.
>
> Это не только устранит циклическую зависимость, но и разместит логику получения данных ближе к тому месту, где она используется, улучшая читаемость и поддерживаемость кода.

### 5.2. Отсутствие механизма отмены в `ShuttleSystem`

Реализация отзыва выявила, что в `ShuttleSystem` **отсутствует встроенный механизм для безопасной отмены FTL-прыжка**, который уже начался.

**Почему это проблема?**
-   **Недостаточная гибкость**: Ограничивает возможности для других систем (например, аномалий, блокирующих FTL).
-   **Риск ошибок**: Разработчики могут попытаться реализовать отмену "вручную" (например, удалив `FTLComponent`), что почти наверняка приведет к поломке шаттла.

**Как исправить?**
> **Рекомендация:** Добавить в `FTLComponent` флаг отмены (`bool CancelRequested`). Метод `UpdateHyperspace` в `ShuttleSystem` должен проверять этот флаг и безопасно прерывать текущее действие, создавая надежный и многоразовый API.

---
## 6. Блокер для независимой эвакуации: Глобальное состояние систем

Углубленный анализ выявил фундаментальную проблему, которая делает невозможной параллельную эвакуацию с нескольких станций. Логика вызова и отсчета времени построена на **глобальном состоянии**, а не на состоянии отдельных станций.

### 6.1. Проблема: Единый таймер на все шаттлы

Ключевые системы (`RoundEndSystem`, `EmergencyShuttleSystem`) используют одиночные переменные для управления всем процессом.

-   В `RoundEndSystem`: поле `private CancellationTokenSource? _countdownTokenSource` является единственным для всех таймеров.
-   В `EmergencyShuttleSystem`: поле `private float _consoleAccumulator` — единственный таймер до отбытия.

**Последствия:** Как только шаттл вызывается для одной станции, система переходит в "занятое" состояние. Последующие вызовы для других станций игнорируются.

Наличие этой проблемы подтверждается комментарием разработчика в файле `EmergencyShuttleSystem.Console.cs`:
> `// Realistically most of this shit needs moving to a station component so each station has their own emergency shuttle`
> `// and timer and all that jazz...`

Этот комментарий прямо указывает, что система не была рассчитана на несколько станций и требует рефакторинга.

### 6.2. Комплексное решение: Переход к состоянию на уровне станции

Чтобы обеспечить поддержку независимой эвакуации, необходим значительный рефакторинг.

#### Шаг 1: Создание нового компонента состояния

Нужно создать `StationEmergencyStateComponent`, который будет хранить переменные, сейчас являющиеся глобальными.

```csharp
[RegisterComponent]
public sealed partial class StationEmergencyStateComponent : Component
{
    // Из RoundEndSystem
    public CancellationTokenSource? CountdownTokenSource;
    public TimeSpan? ExpectedCountdownEnd;

    // Из EmergencyShuttleSystem
    public float ConsoleAccumulator = float.MinValue;
    public bool Launched;
    public bool EarlyLaunchAuthorized;
}
```

#### Шаг 2: Рефакторинг `RoundEndSystem`

-   Удалить глобальные поля.
-   Изменить `RequestRoundEnd`, чтобы она принимала `EntityUid` станции и работала с полями `StationEmergencyStateComponent` для этой станции.

#### Шаг 3: Рефакторинг `EmergencyShuttleSystem`

-   Удалить глобальные поля.
-   Основной цикл `UpdateEmergencyConsole` должен итерировать по всем станциям с `StationEmergencyStateComponent` и обновлять таймер для каждой индивидуально.

---

## 7. Итоговые выводы

1.  **Немедленное исправление:** Проблема с отзывом шаттла решается реализацией метода `RecallShuttle()` в `EmergencyShuttleSystem` и его вызовом из `RoundEndSystem`. Это восстановит базовую функциональность для одного шаттла.
2.  **Долгосрочная стратегия:** Для поддержки нескольких станций и повышения стабильности системы необходим глубокий рефакторинг:
    *   **Устранение глобального состояния:** Перенос логики таймеров и состояний эвакуации из глобальных полей систем в новый `StationEmergencyStateComponent`.
    *   **Разрыв циклических зависимостей:** Рефакторинг `EmergencyShuttleSystem` для независимого получения данных о станциях.
    *   **Создание API для отмены FTL:** Доработка `ShuttleSystem` для безопасной отмены прыжков.

Только комплексное решение позволит создать масштабируемую и надежную систему эвакуации, готовую к будущим расширениям игрового мира.
