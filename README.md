# minibackup

A multi-threaded CLI backup utility for Windows that copies a source directory tree to a destination in parallel, with retry, cancellation, and resource-safety built in.

Built as a learning project to exercise .NET concurrency, streaming I/O, exception handling, and system design end-to-end.

## What it does

- Recursively copies every file from a source directory to a destination directory, preserving relative structure
- Copies files **in parallel**, with a configurable degree of concurrency
- **Retries** transient failures (network blips, sharing violations) with exponential backoff; fails fast on permanent failures (disk full, permission denied)
- Supports **graceful cancellation** via Ctrl+C — in-flight copies are given the chance to unwind cleanly, partial/temporary files are cleaned up
- Streams file contents rather than loading them into memory, so large files (multi-GB) don't blow up memory usage
- Logs per-file success/failure to the console, including full exception details (type, HResult, message, stack trace) for diagnosing *why* a file failed

## Usage

```
minibackup.exe <SourcePath> <DestinationPath>
```

Example:

```
minibackup.exe C:\Data D:\Backup
```

Requires exactly two arguments; both must be non-empty paths. Press **Ctrl+C** at any point to cancel — the program will stop starting new file copies and let in-flight copies wind down before exiting.

## Architecture

```
Program.cs          Entry point: parses CLI args, constructs Orchestrator, catches unhandled exceptions
Orchestrator.cs      Walks the source tree, drives parallel execution, wires cancellation, logs results
CopyWorker.cs         Copies a single file: streaming copy, retry/backoff, atomic move, cleanup on failure
```

### `Orchestrator`

- **`WalkTree`** — lazily enumerates every file under the source path (`Directory.EnumerateFiles` + `yield return`), converting each full path to a path relative to the source root via `Path.GetRelativePath`. Laziness means files start copying before the entire tree has even finished being scanned.
- **`RunAsync`** — wires `Console.CancelKeyPress` to a `CancellationTokenSource`, then drives the copy via `Parallel.ForEachAsync` with a configurable `MaxDegreeOfParallelism`. Each file copy runs in its own try/catch: known failures (`FileCopyFailedException`) are logged and the loop continues to the next file; cancellation and truly unexpected exceptions propagate and stop the run.
- **Logging** — a single shared lock (`System.Threading.Lock`, .NET 9+) guards all console writes, since `Console` is a single process-wide resource regardless of how many files are being copied concurrently. Error logs include exception type, `HResult` (hex), message, and stack trace — walking the full `InnerException` chain — specifically so retry-classification decisions (see below) can be verified after the fact from the log alone.

### `CopyWorker`

- Copies one file via `FileStream` + `Stream.CopyToAsync`, streaming through a 64 KB buffer rather than loading the whole file into memory.
- Writes to a `.tmp` file first, then atomically `File.Move`s it into place on success — so a destination file only ever exists in its final, complete form; a half-copied file is never visible at the real destination path. Cleanup (`.tmp` deletion) runs in a `finally` block on any failure path, including cancellation.
- **Retry logic**: on `IOException`, classifies the failure via `HResult` → Win32 error code (`ERROR_SHARING_VIOLATION`, `ERROR_NETNAME_DELETED`, etc. are treated as transient and retried with exponential backoff; `ERROR_HANDLE_DISK_FULL` and similar are treated as permanent and fail immediately). See the Win32 error code table inside `CopyWorker.cs` for the full classification and the reasoning behind each.
- Both streams are reset to position 0 (and the destination truncated via `SetLength(0)`) before each retry attempt, so a failure partway through a copy doesn't leave a corrupted resume point — retries redo the whole file rather than attempting a partial resume.

## Known limitations

- **Windows-only retry classification.** `IsExceptionTransient` inspects `HResult` assuming Win32 error codes (`HResult & 0xFFFF`). This does not work correctly on Linux/macOS, where `IOException.HResult` maps from `errno` instead.
- **Empty directories are not preserved in the backup.** Since the tree walk only ever yields file paths, a directory containing no files anywhere in its subtree is never created at the destination. This is a deliberate simplification, not a bug.
- **Retries redo the whole file, not just the failed chunk.** For very large files, a late-stage transient failure means re-copying from the start rather than resuming from a checkpoint. A future optimization would track exact bytes written and resume from there.
- **Configuration is currently hardcoded**, not externalized. Buffer size, retry count, and backoff delays are constants in `CopyWorker`; degree of parallelism is a constant in `Orchestrator`. A `configuration.json` + `IOptions<T>`-based setup is planned but not yet implemented.
- **Not crash-safe against process/machine termination.** `File.Move`'s atomicity only holds within a single volume; a cross-volume move (the common case for a backup destination) internally falls back to copy-then-delete, which is not atomic across a power loss or forced termination mid-move. True crash-safety would require a write-ahead journal, which is out of scope for this project.
- **No live in-file progress reporting.** Progress is reported per completed file; a very large file gives no feedback until it finishes (or fails).

## Testing

See [`tests/manual/README.md`](tests/manual/README.md) for the manual end-to-end test harness — generates a realistic mix of large/medium/small files and nested/empty directories, runs the backup, and verifies the result via hash comparison.

No automated unit/integration test project exists yet; this is tracked as future work.

## Requirements

- .NET 10 SDK
- Windows (see [Known limitations](#known-limitations) regarding cross-platform support)

## Project layout

```
minibackup/
├── minibackup.slnx
├── minibackup/
│   ├── minibackup.csproj
│   ├── Program.cs
│   ├── Orchestrator.cs
│   └── CopyWorker.cs
└── tests/
    └── manual/
        ├── README.md
        ├── run-full-test.ps1
        └── utils/
            ├── generate-test-data.ps1
            ├── run-backup.ps1
            └── verify-backup.ps1
```
