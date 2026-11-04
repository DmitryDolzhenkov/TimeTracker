# Time Tracker

Таймер учёта времени для Windows: живёт в трее и сам реагирует на простой, блокировку и выключение. Устройство описано в [DESIGN.md](DESIGN.md).

## Запуск

Нужен .NET SDK 8 или новее и Windows Desktop Runtime 8.

```
dotnet run --project src/TimeTracker.App
dotnet test
```

Крестик в окне сворачивает приложение в трей. Выйти можно через меню значка в трее.

## Данные и настройка

Журнал `events.jsonl` и файл `heartbeat.txt` лежат в `%LOCALAPPDATA%\TimeTrackerSimple\`.

Параметры задаются в `src/TimeTracker.App/appsettings.json` (секция `Tracker`) или переменными окружения:

| Параметр | По умолчанию | Переменная окружения |
|---|---|---|
| `IdleThreshold` | `00:15:00` | `Tracker__IdleThreshold` |
| `PollInterval` | `00:00:05` | `Tracker__PollInterval` |
| `HeartbeatInterval` | `00:00:30` | `Tracker__HeartbeatInterval` |
| `DataDirectory` | `%LOCALAPPDATA%\TimeTrackerSimple` | `Tracker__DataDirectory` |

## Известные ограничения прототипа

- **Сон и гибернация не обрабатываются.** Если компьютер уснул в `Active` без блокировки экрана и его разбудили вводом, время сна засчитается как рабочее.
- **Отмена выключения.** Приложение завершается уже на `SessionEnding`. Если другая программа отменит выключение, Time Tracker нужно запустить заново.
- **Лог диагностики** пишется только в отладочный вывод (`Debug`). Файла с диагностикой нет.
- Корректировка доступна только за текущий день.
