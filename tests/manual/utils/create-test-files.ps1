param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath
)

function Create-RandomFile {
  param (
    [string] $Name,
    [int] $ChunkSizeInMB,
    [int] $Chunks
  )

  $chunkSize = $ChunkSizeInMB * 1024 * 1024

  $rng = New-Object System.Random
  $buffer = New-Object byte[] ($chunkSize)
  
  try {
    $stream = [System.IO.File]::Create($Name)

    for ($i = 0; $i -lt $Chunks; $i++) {
      $rng.NextBytes($buffer)
      $stream.Write($buffer, 0, $buffer.Length)
    }
  }
  finally {
    $stream.Close()
  }
}

function Create-FilesToCopy {
  param (
    [string] $Directory
  )

  # 1 GB files
  New-Item -ItemType Directory -Path "$Directory\LargeFiles" | Out-Null

  1..5 | ForEach-Object {
    Create-RandomFile -Name "$Directory\LargeFiles\large_$_.bin" -ChunkSizeInMB 64 -Chunks 16
  }
  
  Write-Host "[CREATED] Large files 5 x 1GB: LargeFiles\..."

  # 64 MB files
  New-Item -ItemType Directory -Path "$Directory\MediumFiles" | Out-Null

  1..10 | ForEach-Object {
    Create-RandomFile -Name "$Directory\MediumFiles\medium_$_.bin" -ChunkSizeInMB 64 -Chunks 1
  }
  
  Write-Host "[CREATED] Medium files 10 x 64 MB: MediumFiles\..."

  # A lot of small files
  New-Item -ItemType Directory -Path "$Directory\SmallFiles" | Out-Null

  "test content 0" | Out-File "$Directory\small_0.txt"

  1..500 | ForEach-Object {
    Create-RandomFile -Name "$Directory\SmallFiles\small_$_.bin" -ChunkSizeInMB 1 -Chunks 1
  }
  
  Write-Host "[CREATED] Small files 501 x 1 MB: SmallFiles\..."

  # Nested folders
  New-Item -ItemType Directory -Path "$Directory\Nested1\Nested2" | Out-Null
  New-Item -ItemType File -Path "$Directory\Nested1\Nested2\nested.txt" | Out-Null
  Write-Host "[CREATED] Nested file: Nested1\Nested2\nested.txt"

  # An empty subdirectory
  New-Item -ItemType Directory -Path "$Directory\Nested1\EmptyDirectory" | Out-Null
  Write-Host "[CREATED] Empty directory: Nested1\EmptyDirectory"
}

Write-Host "Creating files to copy..."
Create-FilesToCopy -Directory $SourcePath