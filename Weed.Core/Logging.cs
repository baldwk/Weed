using Weed.Abstractions;

namespace Weed.Core;

public sealed record FileWeedLoggerOptions
{
    public long MaxFileBytes { get; init; } = 2 * 1024 * 1024;

    public int RetentionDays { get; init; } = 14;
}

public sealed class FileWeedLogger : IWeedLogger
{
    private static readonly object GlobalGate = new();
    private readonly string _logDirectory;
    private readonly FileWeedLoggerOptions _options;
    private readonly string _scope;
    private DateOnly? _lastCleanupDate;

    public FileWeedLogger(string logDirectory, string scope = "Host", FileWeedLoggerOptions? options = null)
    {
        Directory.CreateDirectory(logDirectory);
        _logDirectory = logDirectory;
        _scope = scope;
        _options = options ?? new FileWeedLoggerOptions();
    }

    public string LogDirectory => _logDirectory;

    public string CurrentLogFile => ResolveLogFile(DateTimeOffset.Now);

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public IWeedLogger ForScope(string scope) => new FileWeedLogger(_logDirectory, scope, _options);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] [{_scope}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (GlobalGate)
        {
            CleanupOldLogsIfNeeded();
            File.AppendAllText(ResolveLogFile(DateTimeOffset.Now), line + Environment.NewLine);
        }
    }

    private string ResolveLogFile(DateTimeOffset now)
    {
        var date = now.ToString("yyyyMMdd");
        var path = Path.Combine(_logDirectory, $"weed-{date}.log");
        if (!ShouldUseNextFile(path))
        {
            return path;
        }

        for (var index = 1; index < 1000; index++)
        {
            path = Path.Combine(_logDirectory, $"weed-{date}-{index}.log");
            if (!ShouldUseNextFile(path))
            {
                return path;
            }
        }

        return Path.Combine(_logDirectory, $"weed-{date}-{Guid.NewGuid():N}.log");
    }

    private bool ShouldUseNextFile(string path) =>
        _options.MaxFileBytes > 0 &&
        File.Exists(path) &&
        new FileInfo(path).Length >= _options.MaxFileBytes;

    private void CleanupOldLogsIfNeeded()
    {
        if (_options.RetentionDays <= 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_lastCleanupDate == today)
        {
            return;
        }

        _lastCleanupDate = today;
        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        foreach (var path in Directory.EnumerateFiles(_logDirectory, "weed-*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Log cleanup should never interrupt the app.
            }
        }
    }
}
