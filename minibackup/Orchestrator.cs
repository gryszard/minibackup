namespace minibackup;

internal class Orchestrator
{
    // TODO: To be configured externally.
    private readonly string SourcePath = "C:\\";
    private readonly string DestinationPath = "D:\\";
    private readonly int MaxDegreeOfParallelism = 4;

    private ConsoleCancelEventHandler? _consoleCancelEventHandler;
    private CancellationTokenSource? _cancellationTokenSource;

    public async Task Run()
    {
        try
        {
            var cancellationToken = WireCancellationTokenToConsole();

            var relativeFilePathsToCopy = WalkTree(SourcePath, cancellationToken);
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(relativeFilePathsToCopy, parallelOptions, async (relativeFilePath, iterationToken) =>
            {
                try
                {
                    var copyWorker = new CopyWorker();
                    await copyWorker.CopyFileAsync(relativeFilePath, SourcePath, DestinationPath, iterationToken);
                    LogSuccess(relativeFilePath, iterationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (FileCopyFailedException ex)
                {
                    LogError(relativeFilePath, ex, iterationToken);
                }
                catch (Exception ex)
                {
                    LogError(relativeFilePath, ex, iterationToken);
                    throw;
                }
            });
        }
        finally
        {
            // Orchestrator.Run() is called once and the program exits right after
            // (typical for a CLI backup tool — run, finish, process ends),
            // the subscription's lifetime is bounded by the process anyway.
            // Nothing meaningfully "leaks" in a way that matters, because the whole app domain is torn down moments later.
            //
            // However, unwire it in finally not because this specific program needs it, but because:
            // 1. It's the correct, defensive pattern regardless of current usage
            //    — you don't want correctness to depend on "well, the process happens to exit right after."
            // 2. It's a good habit
            //    — "did they clean up what they wired up" even when the immediate consequence is small.
            UnwireCancellationTokenFromConsole();
        }
    }

    private CancellationToken WireCancellationTokenToConsole()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        _consoleCancelEventHandler = (s, e) =>
        {
            e.Cancel = true;
            _cancellationTokenSource.Cancel();
        };

        Console.CancelKeyPress += _consoleCancelEventHandler;

        return _cancellationTokenSource.Token;
    }

    private void UnwireCancellationTokenFromConsole()
    {
        if (_consoleCancelEventHandler is not null)
        {
            Console.CancelKeyPress -= _consoleCancelEventHandler;
        }

        _cancellationTokenSource?.Dispose();
        _consoleCancelEventHandler = null;
        _cancellationTokenSource = null;
    }

    private static IEnumerable<string> WalkTree(string basePath, CancellationToken ct)
    {
        foreach (var filePath in Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            // A single MoveNext() call can be slow (slow network share) and nothing inside it checks the token
            // (it's an old, pre-async API). There's no way to interrupt the inner operation.
            // Ctrl+C wouldn't take effect until that one slow OS call finishes and control returns to your foreach.
            // This is a real limitation of the API, not something fixable by adding a check or code-arounds.
            yield return filePath;
        }
    }

    private void LogSuccess(string relativeFilePath, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    private void LogError(string relativeFilePath, Exception ex, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
