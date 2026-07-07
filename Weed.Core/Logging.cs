using Weed.Abstractions;

namespace Weed.Core;

public sealed class FileWeedLogger : IWeedLogger
{
    private readonly object _gate = new();
    private readonly string _logFile;
    private readonly string _scope;

    public FileWeedLogger(string logDirectory, string scope = "Host")
    {
        Directory.CreateDirectory(logDirectory);
        _logFile = Path.Combine(logDirectory, $"weed-{DateTimeOffset.Now:yyyyMMdd}.log");
        _scope = scope;
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public IWeedLogger ForScope(string scope) => new FileWeedLogger(Path.GetDirectoryName(_logFile)!, scope);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] [{_scope}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_gate)
        {
            File.AppendAllText(_logFile, line + Environment.NewLine);
        }
    }
}
