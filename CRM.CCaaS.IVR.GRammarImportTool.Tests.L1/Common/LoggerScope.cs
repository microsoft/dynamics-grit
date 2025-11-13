// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CRM.CCaaS.IVR.GRammarImportTool.Tests.L1.Common;
internal class LoggerScope
{
    private static readonly AsyncLocal<Scope?> _currentScope = new();

    public static IDisposable Push(object? state)
    {
        var parent = _currentScope.Value;
        var newScope = new Scope(state, parent);
        _currentScope.Value = newScope;
        return newScope;
    }

    public static IEnumerable<object?>? Current
    {
        get
        {
            var scope = _currentScope.Value;
            while (scope != null)
            {
                yield return scope.State;
                scope = scope.Parent;
            }
        }
    }

    private sealed class Scope : IDisposable
    {
        public object? State { get; }
        public Scope? Parent { get; }

        public Scope(object? state, Scope? parent)
        {
            State = state;
            Parent = parent;
        }

        public void Dispose()
        {
            if (_currentScope.Value == this)
            {
                _currentScope.Value = Parent;
            }
        }
    }
}
