using System.Collections.Concurrent;
using System.Text;

namespace ErongoIT.Backup.Agent.Logging;

/// <summary>
/// Minimal daily-rolling file logger: logs\agent-YYYYMMDD.log.
/// Keeps 14 days. Needed because a Windows service has no console.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private DateTime _lastCleanupDate = DateTime.MinValue;

    public FileLoggerProvider(string directory, string prefix)
    {
        _directory = directory;
        _prefix = prefix;

        try
        {
            Directory.CreateDirectory(_directory);
        }
        catch
        {
            // Logging must never stop the Agent.
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    public void Dispose() => _loggers.Clear();

    internal void Write(string line)
    {
        try
        {
            var today = DateTime.Now.Date;
            var path = Path.Combine(_directory, $"{_prefix}-{today:yyyyMMdd}.log");

            lock (_lock)
            {
                File.AppendAllText(path, line, Encoding.UTF8);

                if (_lastCleanupDate != today)
                {
                    _lastCleanupDate = today;

                    foreach (var old in Directory.GetFiles(_directory, $"{_prefix}-*.log"))
                    {
                        if (File.GetLastWriteTime(old) < today.AddDays(-14))
                            File.Delete(old);
                    }
                }
            }
        }
        catch
        {
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Information &&
            !_category.StartsWith("LogicalHandler", StringComparison.Ordinal) &&
            !_category.StartsWith("ClientHandler", StringComparison.Ordinal);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var level = logLevel switch
            {
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT",
                _ => "INFO"
            };

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level,-5} [{_category}] {formatter(state, exception)}{Environment.NewLine}";

            if (exception is not null)
                line += exception + Environment.NewLine;

            _provider.Write(line);
        }
    }
}
