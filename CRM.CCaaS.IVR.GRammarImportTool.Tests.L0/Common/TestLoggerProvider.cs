using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L0.Common;

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
}
public class TestLogger : ILogger
{
    private readonly List<string> _loggedMessages = [];

    public IDisposable? BeginScope<TState>(TState state) => null;

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
                return [.. _loggedMessages]; // Return a copy of the logged messages. This is to avoid exception if new message is logged while iteration.
            }
        }
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (formatter != null)
        {
            var message = formatter(state, exception);
            lock (_loggedMessages)
            {
                _loggedMessages.Add($"{logLevel} - {message}");
            }
        }
    }
}
