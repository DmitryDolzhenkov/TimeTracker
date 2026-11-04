namespace TimeTracker.Core;

public enum IdleSignal { None, IdleDetected, InputResumed }

/// <summary>
/// Превращает показания 32-битного счётчика тиков в сигналы о простое.
/// Без состояния ОС, поэтому переполнение счётчика можно проверить тестом.
/// </summary>
public sealed class IdleDetector(TimeSpan threshold)
{
    private uint? _idleSinceTick;

    public IdleSignal Evaluate(uint lastInputTick, uint nowTick, out TimeSpan idle)
    {
        // Вычитание по uint корректно переживает переполнение раз в ~49.7 суток.
        var elapsed = unchecked(nowTick - lastInputTick);

        // Счётчики читаются не атомарно: ввод мог оказаться "позже" now, и разность уйдёт через ноль.
        idle = TimeSpan.FromMilliseconds(elapsed > int.MaxValue ? 0 : elapsed);

        if (_idleSinceTick is { } tick)
        {
            // Сравниваем сам тик, а не idle < интервала опроса: задержка таймера не приведёт к пропуску ввода.
            if (tick == lastInputTick)
                return IdleSignal.None;

            _idleSinceTick = null;
            return IdleSignal.InputResumed;
        }

        if (idle < threshold)
            return IdleSignal.None;

        _idleSinceTick = lastInputTick;
        return IdleSignal.IdleDetected;
    }
}
