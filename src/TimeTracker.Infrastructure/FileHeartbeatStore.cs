using System.Globalization;
using TimeTracker.Core.Abstractions;

namespace TimeTracker.Infrastructure;

public sealed class FileHeartbeatStore : IHeartbeatStore
{
    private readonly string _path;

    public FileHeartbeatStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Write(DateTimeOffset at)
    {
        // Запись через временный файл: сбой посреди записи не испортит предыдущее значение.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, at.ToString("O", CultureInfo.InvariantCulture));
        File.Move(temp, _path, overwrite: true);
    }

    public DateTimeOffset? Read()
    {
        if (!File.Exists(_path))
            return null;

        return DateTimeOffset.TryParse(File.ReadAllText(_path), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;
    }
}
