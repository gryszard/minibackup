# minibackup — Manual Test Harness

This folder contains a manual, end-to-end test harness for `minibackup.exe`. It generates a realistic mix of test data, runs the backup program against it, and verifies the result byte-for-byte.

This is **not** a substitute for automated unit tests (none exist yet — see [Future Work](#future-work)). It's designed to exercise real filesystem behavior — large files, thousands of small files, nested/empty directories — that's awkward or impossible to fully simulate in an isolated unit test.

## Folder layout

```
tests/manual/
├── README.md                   ← this file
├── run-full-test.ps1           ← entry point; runs the full pipeline end-to-end
└── utils/
    ├── generate-test-data.ps1  ← creates a realistic source directory tree
    ├── run-backup.ps1          ← invokes minibackup.exe against the generated data
    └── verify-backup.ps1       ← compares source vs. destination and reports pass/fail
```

## Prerequisites

- `minibackup.exe` already built (`dotnet build` / `dotnet publish` from the `minibackup` project).
- Two **empty** directories available to use as source and destination — they must be empty, or `run-full-test.ps1` will refuse to proceed (to avoid overwriting or misinterpreting pre-existing data).
- Enough free disk space at the source location for the generated test data (roughly **6.11 GB** — see [Generated Data](#generated-data-shape) below).

## Usage

Run the full pipeline from the `tests/manual` directory:

```powershell
.\run-full-test.ps1 -SourcePath "E:\copy" -DestinationPath "F:\copy" -DirWithExe "C:\build\minibackup"
```

| Parameter | Meaning |
|---|---|
| `-SourcePath` | Empty directory where test data will be generated and backed up **from** |
| `-DestinationPath` | Empty directory where `minibackup.exe` will back up **to** |
| `-DirWithExe` | Directory containing `minibackup.exe` (also where `result.txt` log output is written) |

This runs, in order:
1. **`generate-test-data.ps1`** — populates `$SourcePath` with test files (see below)
2. **`run-backup.ps1`** — runs `minibackup.exe $SourcePath $DestinationPath`, redirecting all output streams to `result.txt`
3. **`verify-backup.ps1`** — walks both directories, compares file hashes, and reports pass/fail

There is a `$LASTEXITCODE` check in `run-backup.ps1` (step 2.) after invoking `minibackup.exe`, so a crashed or non-zero-exit run fails fast with a clear message instead of proceeding into verification (step 3.) against an incomplete backup.

### Running steps individually

Each script under `utils/` can also be run standalone, useful for iterating on one stage without regenerating gigabytes of test data every time:

```powershell
.\utils\generate-test-data.ps1 -SourcePath "E:\copy"
.\utils\run-backup.ps1 -SourcePath "E:\copy" -DestinationPath "F:\copy" -PathToExe "C:\...\minibackup.exe" -PathToLogFile "C:\...\result.txt"
.\utils\verify-backup.ps1 -SourcePath "E:\copy" -DestinationPath "F:\copy"
```

## Generated data shape

`generate-test-data.ps1` creates the following under `$SourcePath`, designed to exercise a realistic mix of file sizes and directory shapes:

| Category | Contents | Purpose |
|---|---|---|
| `LargeFiles\` | 5 × 1 GB random-content files | Tests streaming copy, long-running transfers, retry-on-large-file behavior |
| `MediumFiles\` | 10 × 64 MB random-content files | Mid-range file size, useful for parallelism/throughput observation |
| `SmallFiles\` | 500 × 1 MB random-content files + 1 plain-text file | Stresses per-file overhead (directory creation, logging, lock contention) at high file count |
| `Nested1\Nested2\` | 1 small text file, two directory levels deep | Tests destination directory-tree recreation |
| `Nested1\EmptyDirectory\` | Empty directory, no contents | Documents known behavior: **empty directories are not preserved in the backup** (minibackup only creates destination folders as a side effect of copying files into them) |

Total generated size: roughly **5 GB (large) + 640 MB (medium) + ~501 MB (small) ≈ 6.11 GB**.

## What `verify-backup.ps1` checks

- Every file in `$SourcePath` exists at the corresponding relative path in `$DestinationPath`
- File content matches via hash comparison (SHA256 by default; override with `-HashAlgorithm`)
- No leftover `.tmp` files at the destination (would indicate an interrupted/failed copy whose cleanup didn't run)
- No unexpected extra files at the destination (informational only — doesn't fail the run, since stale data from a previous test is more likely than an actual bug)

Exits with code `0` on full success, `1` if any file is missing, mismatched, or a `.tmp` leftover is found.

## Known limitations

- **Empty directories are not preserved.** This is expected behavior given how `minibackup` walks the source tree (file-driven, not directory-driven) — not a bug, but worth remembering when interpreting a "successful" verification.
- **`generate-test-data.ps1` will fail loudly (not silently corrupt data) if disk space runs out** — the `try/finally` around file writes ensures the stream handle is always closed, but a failed write due to disk-full will still throw and halt generation.
- **This harness does not currently simulate:** network drops, file locks held by another process, or disk-full at the destination. These are real scenarios `minibackup`'s retry/classification logic is designed to handle, but they aren't exercised automatically here — currently they'd need to be triggered manually alongside a running test (e.g. opening a file in another process mid-run, disabling a network adapter).

## Future Work

- Automated unit/integration tests (e.g. an `xUnit`-based `minibackup.Tests` project) covering `CopyWorker`'s retry/backoff logic and `IsExceptionTransient`'s classification in isolation, without needing real multi-GB files or real network interruptions.
- Dedicated failure-scenario scripts (simulated disk-full, simulated network drop, simulated file lock) rather than relying on manual intervention during a run.
