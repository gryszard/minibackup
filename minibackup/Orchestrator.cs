namespace minibackup;

internal class Orchestrator
{
    // TODO: To be configured externally.
    private readonly string SourcePath = "C:\\";
    private readonly string DestinationPath = "D:\\";
    private readonly int MaxDegreeOfParallelism = 4;

    public async Task Run()
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

    private CancellationToken WireCancellationTokenToConsole()
    {
        throw new NotImplementedException();
    }

    private IEnumerable<string> WalkTree(string basePath, CancellationToken ct)
    {
        throw new NotImplementedException();
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
