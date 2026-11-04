using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TimeTracker.Core.Abstractions;
using TimeTracker.Core.Events;

namespace TimeTracker.Infrastructure;

public sealed class JsonLinesEventStore : IEventStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // чтобы комментарии читались в файле глазами
    };

    private readonly string _path;
    private readonly ILogger<JsonLinesEventStore> _logger;
    private bool _tailChecked;

    public JsonLinesEventStore(string path, ILogger<JsonLinesEventStore> logger)
    {
        _path = path;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Append(TrackerEvent trackerEvent)
    {
        var line = JsonSerializer.Serialize(trackerEvent, SerializerOptions) + "\n";

        // После пропажи питания последняя строка может быть оборвана: новую запись нельзя приклеить к ней.
        if (!_tailChecked && !EndsWithNewLineOrEmpty())
            line = "\n" + line;
        _tailChecked = true;

        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(Encoding.UTF8.GetBytes(line));
        stream.Flush(flushToDisk: true);
    }

    public IReadOnlyList<TrackerEvent> ReadAll()
    {
        if (!File.Exists(_path))
            return [];

        var events = new List<TrackerEvent>();
        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                if (JsonSerializer.Deserialize<TrackerEvent>(line, SerializerOptions) is { } trackerEvent)
                    events.Add(trackerEvent);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Пропущена повреждённая строка журнала: {Line}", line);
            }
        }
        return events;
    }

    private bool EndsWithNewLineOrEmpty()
    {
        if (!File.Exists(_path))
            return true;

        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
            return true;

        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }
}
