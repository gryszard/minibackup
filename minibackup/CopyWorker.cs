namespace minibackup;

internal class CopyWorker
{
    // TODO: Change into external configuration.
    private readonly int[] BackoffTimesInMilliseconds = [100, 1000, 5000];

    // Typical defaults are 64–81 KB
    private const int BufferSizeInBytes = 1024 * 64;

    public async Task CopyFileAsync(string fileRelativePath, string sourcePath, string destinationPath, CancellationToken ct)
    {
        var destinationTempFilePath = Path.Combine(destinationPath, fileRelativePath + ".tmp");
        var destinationFilePath = Path.Combine(destinationPath, fileRelativePath);
        var sourceFilePath = Path.Combine(sourcePath, fileRelativePath);

        ThrowIfFileNotExists(sourceFilePath);
        ThrowIfFileAlreadyExists(destinationFilePath);
        ThrowIfFileAlreadyExists(destinationTempFilePath);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationTempFilePath)!);

        (bool IsSuccess, Exception? Error) copyResult = (false, null);
        bool operationSucceeded = false;

        try
        {
            using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read))
            using (var destinationStream = new FileStream(destinationTempFilePath, FileMode.CreateNew, FileAccess.Write))
            {
                copyResult = await CopyToWithRetryAsync(sourceStream, destinationStream, ct);

                if (!copyResult.IsSuccess)
                {
                    throw new FileCopyFailedException("File copy failed", copyResult.Error);
                }

                try
                {
                    File.Move(destinationTempFilePath, destinationFilePath);
                    operationSucceeded = true;
                }
                catch (IOException ex)
                {
                    throw new FileCopyFailedException("Cannot move temporary file into final path", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            if (!operationSucceeded)
            {
                CleanupTemporaryFiles(destinationTempFilePath, destinationFilePath);
            }
        }
    }

    private void CleanupTemporaryFiles(string tempFile, string finalFile)
    {
        // File.Delete on a missing file does not throw — it's a no-op if the file doesn't exist.
        File.Delete(tempFile);

        // Deleting finalFile in cleanup is dead code in the common case,
        // but it's cheap defensive insurance against the rare cross-volume-move-interrupted edge case.
        //
        // File.Move is only atomic within the same volume
        // 1. Same-volume move (e.g. C:\source\file.txt → C:\backup\file.txt, same physical drive):
        //    this is a directory-entry rename at the filesystem level — no data is copied, just metadata pointers are repointed.
        //    It's a single atomic operation. It either fully happens or fully doesn't. No partial state is possible.
        // 2. Cross-volume move (e.g. C:\source\file.txt → D:\backup\file.txt, or a network share, 
        //    or any case where source and destination live on different underlying volumes): 
        //    the OS cannot just repoint metadata, because the data physically has to move between two separate storage devices. 
        //    So .NET's File.Move transparently falls back to copy the bytes to the new location, then delete the original 
        //    — internally, roughly File.Copy + File.Delete.
        File.Delete(finalFile);
    }

    private async Task<(bool IsSuccess, Exception? Error)> CopyToWithRetryAsync(Stream sourceStream, Stream destinationStream, CancellationToken ct)
    {
        Exception? lastException = null;

        foreach (var backoffTimeInMilliseconds in BackoffTimesInMilliseconds)
        {
            try
            {
                sourceStream.Seek(0, SeekOrigin.Begin);
                destinationStream.Seek(0, SeekOrigin.Begin);
                destinationStream.SetLength(0);

                await sourceStream.CopyToAsync(destinationStream, BufferSizeInBytes, ct);
                return (true, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex)
            {
                lastException = ex;

                if (IsExceptionTransient(ex))
                {
                    await Task.Delay(backoffTimeInMilliseconds, ct);
                    continue;
                }

                return (false, ex);
            }
        }

        return (false, lastException);
    }

    private static void ThrowIfFileNotExists(string filePath)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        throw new InvalidOperationException($"File does not exist: {filePath}");
    }

    private static void ThrowIfFileAlreadyExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        throw new InvalidOperationException($"File already exists: {filePath}");
    }

    private bool IsExceptionTransient(IOException ex)
    {
        throw new NotImplementedException();
    }
}

internal class FileCopyFailedException : Exception
{
    public FileCopyFailedException()
    {
    }

    public FileCopyFailedException(string? message) : base(message)
    {
    }

    public FileCopyFailedException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}