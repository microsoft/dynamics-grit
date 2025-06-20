using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L1;
internal class TestConsoleWriter(ITestOutputHelper output) : TextWriter
{
    private readonly ITestOutputHelper _output = output;
    private string _buffer = string.Empty;

    public override Encoding Encoding
    {
        get { return Encoding.UTF8; }
    }
    public override void WriteLine(string message)
    {
        _output.WriteLine(message);
    }
    public override void WriteLine(string format, params object[] args)
    {
        _output.WriteLine(format, args);
    }

    public override void Write(char value)
    {
        if (value == '\r')
            return; // Ignore '\r' characters
        if (value == '\n')
        {
            _output.WriteLine(_buffer);
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
    }

    public override void Write(string format, params object[] args)
    {
        _output.WriteLine(format, args);
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
