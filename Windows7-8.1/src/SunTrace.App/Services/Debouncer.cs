namespace SunTrace.App.Services;

public sealed class Debouncer
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cancellation;

    public Debouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    public void Run(Action action) => Run(() =>
    {
        action();
        return Task.CompletedTask;
    });

    public void Run(Func<Task> action)
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _cancellation, next);
        previous?.Cancel();
        previous?.Dispose();
        _ = ExecuteAsync(action, next.Token);
    }

    public void Cancel()
    {
        var previous = Interlocked.Exchange(ref _cancellation, null);
        previous?.Cancel();
        previous?.Dispose();
    }

    private async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(true);
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
