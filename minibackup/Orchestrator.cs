namespace minibackup;

internal class Orchestrator
{
    // TODO: To be configured externally.
    private readonly string SourcePath;
    private readonly string DestinationPath;
    private readonly int MaxDegreeOfParallelism = 4;

    private readonly static Lock _printLock = new();

    private int _successCount = 0;
    private int _errorCount = 0;

    private ConsoleCancelEventHandler? _consoleCancelEventHandler;
    private CancellationTokenSource? _cancellationTokenSource;

    public Orchestrator(string sourcePath, string destinationPath)
    {
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
    }

    public async Task RunAsync()
    {
        try
        {
            LogInfo($"RunAsync started with source: {SourcePath} and destination: {DestinationPath}");

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
                    LogInfo($"Trying to copy file {relativeFilePath}");

                    var copyWorker = new CopyWorker();
                    await copyWorker.CopyFileAsync(relativeFilePath, SourcePath, DestinationPath, iterationToken);
                    LogSuccess(relativeFilePath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (FileCopyFailedException ex)
                {
                    LogError(relativeFilePath, ex);
                }
                catch (Exception ex)
                {
                    LogError(relativeFilePath, ex);
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

            var relativeFilePath = Path.GetRelativePath(basePath, filePath);

            // A single MoveNext() call can be slow (slow network share) and nothing inside it checks the token
            // (it's an old, pre-async API). There's no way to interrupt the inner operation.
            // Ctrl+C wouldn't take effect until that one slow OS call finishes and control returns to your foreach.
            // This is a real limitation of the API, not something fixable by adding a check or code-arounds.
            yield return relativeFilePath;
        }
    }

    private static void LogInfo(string message)
    {
        lock (_printLock)
        {
            Console.WriteLine($"[INFO] {message}");
        }
    }

    private void LogSuccess(string relativeFilePath)
    {
        lock (_printLock)
        {
            _successCount++;
            Console.WriteLine($"[OK] File {_successCount} copied from: {relativeFilePath}");
        }
    }

    private void LogError(string relativeFilePath, Exception ex)
    {
        lock (_printLock)
        {
            _errorCount++;
            Console.WriteLine($"[FAIL] Total errors: {_errorCount}, file: {relativeFilePath}");

            var current = ex;
            var depth = 0;

            // Exception.ToString() is overridden to produce a rich, multi-line block that already includes:
            // the fully-qualified exception type name, the message, the full stack trace,
            // and it recursively walks InnerException for you, prefixing each with "---> "
            // However, Exception.ToString() doesn't include HResult in the default output
            // — so if we switch to Console.WriteLine(ex), we'd lose the exact piece of info
            // added for retry-classification debugging (checking if the IOException is transient).
            while (current is not null)
            {
                var prefix = depth == 0 ? "Exception" : $"Inner exception (depth {depth})";
                Console.WriteLine($"{prefix}: {current.GetType().Name}, HResult: 0x{current.HResult:X8}, message: {current.Message}");
                Console.WriteLine($"Stack trace: {current.StackTrace}");
                Console.WriteLine();

                current = current.InnerException;
                depth++;
            }
        }
    }
}
