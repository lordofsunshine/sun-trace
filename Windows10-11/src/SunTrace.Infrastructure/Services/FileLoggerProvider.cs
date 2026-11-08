using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SunTrace.Infrastructure.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetentionDays = 30;
    private const int QueueCapacity = 2048;
    private const int MaxBatch = 256;

    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly Channel<LogEntry> _queue = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly Task _writer;
    private DateTime _lastCleanup = DateTime.MinValue;

    public FileLoggerProvider(string directory, long maxBytes = 2 * 1024 * 1024)
    {
        _directory = directory;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(_directory);
        CleanupOldLogs();
        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try
        {
            _writer.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
        }
    }

    private void Enqueue(LogEntry entry) => _queue.Writer.TryWrite(entry);

    private async Task WriteLoopAsync()
    {
        var reader = _queue.Reader;
        var builder = new StringBuilder();
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var now = DateTimeOffset.Now;
                var count = 0;
                while (count < MaxBatch && reader.TryRead(out var entry))
                {
                    Append(builder, entry);
                    count++;
                }

                Flush(builder, now);
            }
        }
        catch (Exception)
        {
        }
    }

    private void Flush(StringBuilder builder, DateTimeOffset now)
    {
        if (builder.Length == 0)
        {
            return;
        }

        try
        {
            if (_lastCleanup.Date != now.Date)
            {
                CleanupOldLogs();
            }

            var path = Path.Combine(_directory, $"sunTrace-{now:yyyy-MM-dd}.log");
            RotateIfNeeded(path);
            File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
        }
        catch (Exception)
        {
        }
        finally
        {
            builder.Clear();
        }
    }

    private static void Append(StringBuilder builder, LogEntry entry)
    {
        builder.Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(LevelLabel(entry.Level)).Append("] ");
        builder.Append(entry.Category).Append(": ").Append(entry.Message);
        if (entry.Exception is not null)
        {
            builder.AppendLine().Append(entry.Exception);
        }

        builder.AppendLine();
    }

    private void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }

        var baseName = $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:HHmmss}";
        var archive = Path.Combine(_directory, baseName + ".log");
        var index = 1;
        while (File.Exists(archive))
        {
            archive = Path.Combine(_directory, $"{baseName}-{index}.log");
            index++;
        }

        try
        {
            File.Move(path, archive, overwrite: false);
        }
        catch (Exception)
        {
        }
    }

    private void CleanupOldLogs()
    {
        _lastCleanup = DateTime.Now;
        try
        {
            var threshold = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "sunTrace-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < threshold)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT",
        _ => "NONE"
    };

    private readonly record struct LogEntry(DateTimeOffset Time, LogLevel Level, string Category, string Message, Exception? Exception);

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

#if NET6_0
        public IDisposable BeginScope<TState>(TState state) => null!;
#else
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
#endif

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            _provider.Enqueue(new LogEntry(DateTimeOffset.Now, logLevel, _category, message, exception));
        }
    }
}
