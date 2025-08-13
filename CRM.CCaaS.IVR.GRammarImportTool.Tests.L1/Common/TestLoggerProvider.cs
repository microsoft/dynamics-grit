using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L1.Common;

public class TestLoggerProvider : ILoggerProvider
{
    private bool _disposed;
    public TestLogger Logger { get; private set; } = new TestLogger();

    public ILogger CreateLogger(string categoryName)
    {
        return Logger;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources here.
            }

            // Dispose unmanaged resources here.

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public class TestLogger : ILogger
    {
        private readonly List<string> _loggedMessages = [];

        public IDisposable BeginScope<TState>(TState state)
        {
            return LoggerScope.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel) => true;
        public void Clear()
        {
            lock (_loggedMessages)
            {
                _loggedMessages.Clear();
            }
        }

        public IEnumerable<string> LoggedMessages
        {
            get
            {
                lock (_loggedMessages)
                {
                    return [.. _loggedMessages];
                }
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter != null)
            {
                var message = formatter(state, exception);
                var timestamp = DateTimeOffset.UtcNow;

                var scopeInfo = string.Join(" | ", LoggerScope.Current?.Select(s =>
                {
                    if (s is IEnumerable<KeyValuePair<string, object>> scopeKvps)
                    {
                        return string.Join(", ", scopeKvps
                            .Where(kvp => kvp.Key != "{OriginalFormat}")
                            .Select(kvp => $"{kvp.Key}: {kvp.Value}"));
                    }
                    return s?.ToString();
                }) ?? []);

                lock (_loggedMessages)
                {
                    _loggedMessages.Add($"[{timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")}] [{logLevel.ToString()}] - {message} => {scopeInfo}");
                }
                if (exception != null)
                {
                    _loggedMessages.Add(exception.ToString());
                }
            }
        }
    }
}
