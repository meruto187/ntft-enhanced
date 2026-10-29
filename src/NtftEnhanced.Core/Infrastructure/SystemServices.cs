namespace NtftEnhanced.Infrastructure;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface ILog
{
    void Info(string code);
}

public sealed class FileLog : ILog
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLog(string path) => _path = path;

    public void Info(string code)
    {
        var line = DateTimeOffset.UtcNow.ToString("o") + " " + code.Replace('\r', ' ').Replace('\n', ' ') + "\n";
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, line);
        }
    }
}

public enum ProbeResult
{
    Ok,
    Blocked,
    Unreachable
}

public interface INameProbe
{
    ProbeResult Probe(string host);
}

public interface IDnsFlusher
{
    void Flush();
}

public sealed class NoDnsFlush : IDnsFlusher
{
    public void Flush()
    {
    }
}
