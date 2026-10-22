namespace Wimf.Server;

/// <summary>One shared idle clock and a single asynchronous reload for all browser tabs.</summary>
public sealed class CatalogIdleLifecycle : IAsyncDisposable
{
    private readonly LiveCatalogStore _store;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _monitor;
    private long _lastInput = Environment.TickCount64;
    private Task? _loading;
    private int _activeSearches;

    public CatalogIdleLifecycle(LiveCatalogStore store, TimeSpan timeout)
    {
        _store = store;
        _timeout = timeout;
        _monitor = MonitorAsync();
    }

    public bool IsLoading => _loading is { IsCompleted: false };
    public bool IsUnloaded => _store.IsUnloaded;

    public async Task TouchAsync()
    {
        Interlocked.Exchange(ref _lastInput, Environment.TickCount64);
        await StartReloadIfNeededAsync();
    }

    public async Task<IDisposable> BeginSearchAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastInput, Environment.TickCount64);
        await _gate.WaitAsync(cancellationToken);
        Interlocked.Increment(ref _activeSearches);
        _gate.Release();
        try
        {
            await StartReloadIfNeededAsync().WaitAsync(cancellationToken);
            return new SearchLease(this);
        }
        catch
        {
            Interlocked.Decrement(ref _activeSearches);
            throw;
        }
    }

    private async Task StartReloadIfNeededAsync()
    {
        Task? reload;
        await _gate.WaitAsync();
        try
        {
            if (!_store.IsUnloaded && _loading is not { IsCompleted: false }) return;
            if (_loading is not { IsCompleted: false })
                _loading = Task.Run(() => _store.ReloadAsync(_stop.Token));
            reload = _loading;
        }
        finally { _gate.Release(); }
        await reload;
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                if (Environment.TickCount64 - Interlocked.Read(ref _lastInput) < _timeout.TotalMilliseconds)
                    continue;
                await _gate.WaitAsync(_stop.Token);
                try
                {
                    if (_activeSearches == 0 && _loading is not { IsCompleted: false } &&
                        Environment.TickCount64 - Interlocked.Read(ref _lastInput) >= _timeout.TotalMilliseconds &&
                        !_store.IsUnloaded)
                        await _store.UnloadAsync(_stop.Token);
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private sealed class SearchLease(CatalogIdleLifecycle owner) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref owner._activeSearches);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _monitor; } catch (OperationCanceledException) { }
        _gate.Dispose();
        _stop.Dispose();
    }
}
