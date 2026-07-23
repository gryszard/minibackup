<#
.SYNOPSIS
    Verifies a minibackup run by comparing source and destination directory trees:
    - Every source file exists at the destination (relative path preserved)
    - File hashes match (byte-for-byte content correctness)
    - No unexpected extra files at the destination
    - No leftover .tmp files (would indicate a failed/interrupted copy)

.EXAMPLE
    .\verify-backup.ps1 -SourcePath "C:\TestData\Source" -DestinationPath "D:\TestData\Dest"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,

    [string]$HashAlgorithm = "SHA256"
)

if (-not (Test-Path $SourcePath)) {
    throw "Source path does not exist: $SourcePath"
}
if (-not (Test-Path $DestinationPath)) {
    throw "Destination path does not exist: $DestinationPath"
}

Write-Host "Scanning source: $SourcePath"
$sourceFiles = Get-ChildItem -Path $SourcePath -Recurse -File
Write-Host "Found $($sourceFiles.Count) source files."

$missing = @()
$mismatched = @()
$verified = 0

foreach ($file in $sourceFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($SourcePath, $file.FullName)
    $destFile = Join-Path $DestinationPath $relativePath

    if (-not (Test-Path $destFile)) {
        $missing += $relativePath
        continue
    }

    $sourceHash = (Get-FileHash -Path $file.FullName -Algorithm $HashAlgorithm).Hash
    $destHash = (Get-FileHash -Path $destFile -Algorithm $HashAlgorithm).Hash

    if ($sourceHash -ne $destHash) {
        $mismatched += $relativePath
    }
    else {
        $verified++
    }
}

# Check for leftover .tmp files — indicates an interrupted/failed copy that wasn't cleaned up
Write-Host "Scanning destination for leftover .tmp files..."
$leftoverTemps = Get-ChildItem -Path $DestinationPath -Recurse -File -Filter "*.tmp"

# Check for unexpected extra files at destination (present at dest, absent at source)
Write-Host "Scanning destination for unexpected extra files..."
$destFiles = Get-ChildItem -Path $DestinationPath -Recurse -File | Where-Object { $_.Extension -ne ".tmp" }
$extraFiles = @()

foreach ($destFile in $destFiles) {
    $relativePath = [System.IO.Path]::GetRelativePath($DestinationPath, $destFile.FullName)
    $expectedSourceFile = Join-Path $SourcePath $relativePath
    if (-not (Test-Path $expectedSourceFile)) {
        $extraFiles += $relativePath
    }
}

Write-Host ""
Write-Host "===== Verification Summary ====="
Write-Host "Verified (hash match):    $verified / $($sourceFiles.Count)"
Write-Host "Missing at destination:   $($missing.Count)"
Write-Host "Hash mismatches:          $($mismatched.Count)"
Write-Host "Leftover .tmp files:      $($leftoverTemps.Count)"
Write-Host "Unexpected extra files:   $($extraFiles.Count)"

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "--- Missing files ---" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  $_" }
}

if ($mismatched.Count -gt 0) {
    Write-Host ""
    Write-Host "--- Hash mismatches ---" -ForegroundColor Red
    $mismatched | ForEach-Object { Write-Host "  $_" }
}

if ($leftoverTemps.Count -gt 0) {
    Write-Host ""
    Write-Host "--- Leftover .tmp files ---" -ForegroundColor Red
    $leftoverTemps | ForEach-Object { Write-Host "  $($_.FullName)" }
}

if ($extraFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "--- Unexpected extra files ---" -ForegroundColor Yellow
    $extraFiles | ForEach-Object { Write-Host "  $_" }
}

$success = ($missing.Count -eq 0) -and ($mismatched.Count -eq 0) -and ($leftoverTemps.Count -eq 0)

Write-Host ""
if ($success) {
    Write-Host "RESULT: Backup verified successfully." -ForegroundColor Green
}
else {
    Write-Host "RESULT: Backup verification FAILED." -ForegroundColor Red
    exit 1
}