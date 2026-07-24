param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
	
    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,
	
    [Parameter(Mandatory = $true)]
    [string]$PathToExe,
	
    [Parameter(Mandatory = $true)]
    [string]$PathToLogFile
)

Write-Host "Running backup program..."

# The > redirect captures stdout only. If minibackup.exe throws an unhandled exception, 
# .NET typically writes the unhandled exception's message and stack trace to stderr, not stdout.
# Log file would show a seemingly-successful run, with the actual crash information going to the console instead.
# The *> redirects all streams (output, error, warning, verbose, debug) into the same file.
& "$PathToExe" $SourcePath $DestinationPath *> "$PathToLogFile"

if ($LASTEXITCODE -ne 0) {
    throw "minibackup.exe exited with code $LASTEXITCODE (some files may have failed or the process crashed). See log: $PathToLogFile"
}