using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Xunit.Abstractions;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L1;
internal class TestConsoleWriter(ITestOutputHelper output) : TextWriter
{
    private readonly ITestOutputHelper _output = output;
    private string _buffer = string.Empty;
    private readonly List<string> _lines = new List<string>();
    private readonly object _lock = new object();

    public override Encoding Encoding
    {
        get { return Encoding.UTF8; }
    }
    public override void WriteLine(string? message)
    {
        _output.WriteLine(message);
        _lines.Add(message ?? string.Empty);
    }
    public override void WriteLine(string format, params object?[] args)
    {
        _output.WriteLine(format, args);
        _lines.Add(string.Format(format, args));
    }

    public override void Write(char value)
    {
        if (value == '\r')
            return; // Ignore '\r' characters
        if (value == '\n')
        {
            _output.WriteLine(_buffer);
            _lines.Add(_buffer);
            _buffer = string.Empty;
        }
        else
        {
            _buffer += value;
        }
    }
    public override void Write(string? value)
    {
        _output.WriteLine(value);
        _lines.Add(value ?? string.Empty);
    }

    public override void Write(string format, params object?[] args)
    {
        _output.WriteLine(format, args);
        _lines.Add(string.Format(format, args));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
    public IEnumerable<string> GetLines()
    {
        lock (_lock)
        {
            return [.. _lines];
        }
    }

    public void ClearLines()
    {
        lock (_lock)
        {
            _lines.Clear();
        }
    }

    public void AddLine(string line)
    {
        lock (_lock)
        {
            _lines.Add(line);
        }
    }
}
