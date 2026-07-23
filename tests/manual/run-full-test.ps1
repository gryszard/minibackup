<#
.EXAMPLE
    .\test-happy-path.ps1 -SourcePath "E:\copy" -DestinationPath "F:\copy" -DirWithExe "C:\Users\Ryszard\Desktop\net10.0"
#>

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

.\utils\create-test-files.ps1 -SourcePath $SourcePath

Write-Host "Running backup program..."
& "$($DirWithExe)\minibackup.exe" $SourcePath $DestinationPath > "$($DirWithExe)\result.txt"

.\utils\verify-backup.ps1 -SourcePath $SourcePath -DestinationPath $DestinationPath
