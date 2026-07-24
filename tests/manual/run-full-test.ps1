param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,

    [Parameter(Mandatory = $true)]
    [string]$DirWithExe
)

function Test-DirectoryEmpty {
    param(
      [string]$Path
    )

    # By default, Get-ChildItem hides hidden and system files/folders 
    # (e.g. desktop.ini, Thumbs.db, or a hidden .git folder) 
    # — a directory containing only hidden items would report as "empty" without -Force
    $first = Get-ChildItem -Path $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1
    return ($null -eq $first)
}

if (-not (Test-DirectoryEmpty -Path $SourcePath)) {
  throw "Source directory not empty. Cannot perform test."
}

if (-not (Test-DirectoryEmpty -Path $DestinationPath)) {
  throw "Destination directory not empty. Cannot perform test."
}

& "$PSScriptRoot\utils\generate-test-data.ps1" -SourcePath $SourcePath

& "$PSScriptRoot\utils\run-backup.ps1" -SourcePath $SourcePath -DestinationPath $DestinationPath -PathToExe "$($DirWithExe)\minibackup.exe" -PathToLogFile "$($DirWithExe)\result.txt"

& "$PSScriptRoot\utils\verify-backup.ps1" -SourcePath $SourcePath -DestinationPath $DestinationPath
