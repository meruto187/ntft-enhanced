using System.Text.Json;

namespace NtftEnhanced.Security;

public sealed class AttemptLimiter
{
    public const int FailuresPerLockout = 5;
    public const int InitialLockoutSeconds = 60;
    public const int MaxLockoutSeconds = 3600;

    private readonly string _path;
    private readonly object _gate = new();
    private State _state;

    public AttemptLimiter(string path)
    {
        _path = path;
        _state = Load(path);
    }

    public TimeSpan? Remaining(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_state.LockoutUntil is null || _state.LockoutUntil <= now)
                return null;
            return _state.LockoutUntil.Value - now;
        }
    }

    public TimeSpan? RegisterFailure(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_state.LockoutUntil is DateTimeOffset until && until > now)
                return until - now;

            _state.Failures++;
            TimeSpan? lockout = null;
            if (_state.Failures % FailuresPerLockout == 0)
            {
                var step = _state.Failures / FailuresPerLockout;
                var seconds = LockoutSeconds(step);
                _state.LockoutSeconds = seconds;
                _state.LockoutUntil = now.AddSeconds(seconds);
                lockout = TimeSpan.FromSeconds(seconds);
            }
            Save();
            return lockout;
        }
    }

    public void RegisterSuccess()
    {
        lock (_gate)
        {
            _state = new State();
            Save();
        }
    }

    public static int LockoutSeconds(int step)
    {
        if (step < 1)
            return 0;
        var shift = step - 1;
        if (shift >= 16)
            return MaxLockoutSeconds;
        var seconds = InitialLockoutSeconds * (1 << shift);
        return Math.Min(MaxLockoutSeconds, seconds);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_state));
    }

    private static State Load(string path)
    {
        if (!File.Exists(path))
            return new State();
        try
        {
            return JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State();
        }
        catch (JsonException)
        {
            return new State();
        }
    }

    private sealed class State
    {
        public int Failures { get; set; }
        public DateTimeOffset? LockoutUntil { get; set; }
        public int LockoutSeconds { get; set; }
    }
}
