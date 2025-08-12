using System;
using System.Threading;
using CRM.CCaaS.IVR.GRammarImportTool.Stubs.Models;

namespace CRM.CCaaS.IVR.GRammarImportTool.Stubs.Services;

public sealed class StubBehaviorState : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private StubBehaviorOptions _current;

    public StubBehaviorState(StubBehaviorOptions initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = Clone(initial);
    }

    public StubBehaviorOptions Snapshot()
    {
        _lock.EnterReadLock();
        try
        {
            return Clone(_current);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Update(Action<StubBehaviorOptions> updater)
    {
        ArgumentNullException.ThrowIfNull(updater);

        _lock.EnterWriteLock();
        try
        {
            var copy = Clone(_current);
            updater(copy);
            _current = Sanitize(copy);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Replace(StubBehaviorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _lock.EnterWriteLock();
        try
        {
            _current = Sanitize(Clone(options));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private static StubBehaviorOptions Clone(StubBehaviorOptions o) => new()
    {
        Mode = o.Mode,
        ErrorStatusCode = o.ErrorStatusCode,
        ErrorMessage = o.ErrorMessage,
        BadItemsPercentage = o.BadItemsPercentage,
        MaxCorruptionsPerArray = o.MaxCorruptionsPerArray,
        ProduceMalformedJson = o.ProduceMalformedJson,
        TargetProperties = o.TargetProperties is null ? null : (string[])o.TargetProperties.Clone(),
        AdminApiKey = o.AdminApiKey
    };

    private static StubBehaviorOptions Sanitize(StubBehaviorOptions o)
    {
        if (o.BadItemsPercentage < 0) o.BadItemsPercentage = 0;
        if (o.BadItemsPercentage > 100) o.BadItemsPercentage = 100;
        if (o.MaxCorruptionsPerArray < 0) o.MaxCorruptionsPerArray = 0;
        if (o.ErrorStatusCode is < 400 or > 599) o.ErrorStatusCode = 500;

        if (o.Mode is StubMode.BadResultsAll or StubMode.RefuseConnection)
        {
            o.BadItemsPercentage = 100;
        }

        o.AdminApiKey ??= string.Empty;
        return o;
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}