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
            }

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

    private static bool IsExceptionTransient(IOException ex)
    {
        // HResult's low 16 bits carry the Win32 error code on Windows.
        //
        // Bit 31       Severity (S)    0 = success, 1 = failure
        // Bit 30       Reserved (R)    Historically tied to NTSTATUS conversion
        // Bit 29       Customer (C)    0 = Microsoft-defined facility, 1 = third-party/customer-defined
        // Bit 28       N               Used only when converting from NTSTATUS codes
        // Bit 27       reserved        must be 0
        // Bits 26–16   Facility        Which subsystem produced this — an 11-bit code identifying the origin (Win32, RPC, COM interface, etc.)
        // Bits 15–0    Code            The actual error value within that facility
        var win32ErrorCode = ex.HResult & 0xFFFF;

        return win32ErrorCode switch
        {
            Win32ErrorCodes.ERROR_SHARING_VIOLATION => true,
            Win32ErrorCodes.ERROR_LOCK_VIOLATION => true,
            Win32ErrorCodes.ERROR_NETNAME_DELETED => true,
            Win32ErrorCodes.ERROR_NETWORK_BUSY => true,
            Win32ErrorCodes.ERROR_SEM_TIMEOUT => true,
            Win32ErrorCodes.ERROR_UNEXP_NET_ERR => true,

            Win32ErrorCodes.ERROR_HANDLE_DISK_FULL => false,
            Win32ErrorCodes.ERROR_DISK_FULL => false,

            // Failing fast and surfacing the error (rather than silently swallowing it
            // into "eventually gave up after 3 tries") is the safer default for a backup tool,
            // where silent data loss is far worse than a loud failure.
            _ => false
        };
    }

    private static class Win32ErrorCodes
    {
        // The destination volume runs out of space while writing through an already-open file handle
        // — this is the one you'll actually hit mid-CopyToAsync, since your destinationStream handle is open the whole time
        public const int ERROR_HANDLE_DISK_FULL = 39;

        // Disk-full detected via an older, non-handle-based API path (e.g. during file creation rather than an in-progress write)
        // — legacy from DOS-era API conventions; you're less likely to see this one specifically in your streaming-copy scenario,
        // but you'll hit it if FileMode.CreateNew itself fails due to zero free space
        public const int ERROR_DISK_FULL = 112;

        // You try to open a file another process already has open with a conflicting share mode
        // — e.g. antivirus is mid-scan on it, the user has it open in Excel/Notepad, or another backup job is touching the same file
        public const int ERROR_SHARING_VIOLATION = 32;

        // The file is open, but a specific byte range inside it is locked via explicit LockFile/LockFileEx calls
        // — common with database files, Outlook .pst files, or VM disk images being actively written to
        // while you try to read overlapping bytes
        public const int ERROR_LOCK_VIOLATION = 33;

        // The SMB/network session backing the share got torn down mid-operation
        // — server-side reboot, someone unplugs the network cable, Wi-Fi drops,
        // or the share gets disconnected while your handle was still open
        public const int ERROR_NETNAME_DELETED = 64;

        // The remote server or network stack is temporarily overloaded and can't service the request right now
        // — no actual disconnection, just contention
        public const int ERROR_NETWORK_BUSY = 54;

        // Network semaphore timeout.
        // Very common on network drives specifically — the remote server didn't respond inside the OS's network timeout window;
        // typical on slow VPNs, congested NAS devices, or high-latency WAN links
        public const int ERROR_SEM_TIMEOUT = 121;

        // A generic network-stack failure with no more specific code — often a NIC driver hiccup or a switch/router blip mid-transfer
        public const int ERROR_UNEXP_NET_ERR = 59;
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