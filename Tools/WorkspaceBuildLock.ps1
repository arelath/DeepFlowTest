Set-StrictMode -Version 2.0

function Enter-WorkspaceBuildLock {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Root,

    [Parameter(Mandatory = $true)]
    [TimeSpan]$Timeout,

    [Parameter(Mandatory = $true)]
    [string]$CommandDescription
  )

  if ($Timeout -le [TimeSpan]::Zero) {
    throw "Workspace build lock timeout must be greater than zero."
  }

  $canonicalRoot = [System.IO.Path]::GetFullPath($Root)
  $artifacts = Join-Path $canonicalRoot "artifacts"
  [System.IO.Directory]::CreateDirectory($artifacts) | Out-Null
  $lockPath = Join-Path $artifacts ".workspace-build.lock"
  $ownerPath = Join-Path $artifacts ".workspace-build-owner.json"
  $invocationId = [Guid]::NewGuid().ToString("N")
  $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
  $nextReport = [TimeSpan]::Zero
  $reportedWaiting = $false

  while ($true) {
    $stream = $null
    $temporaryOwnerPath = $null
    try {
      $stream = [System.IO.File]::Open(
        $lockPath,
        [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)

      $owner = [ordered]@{
        invocationId = $invocationId
        processId = $PID
        processStartUtc = ([System.Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"))
        machineName = [Environment]::MachineName
        acquiredUtc = [DateTimeOffset]::UtcNow.ToString("O")
        repositoryRoot = $canonicalRoot
        command = $CommandDescription
      }
      $temporaryOwnerPath = "$ownerPath.$invocationId.tmp"
      [System.IO.File]::WriteAllText(
        $temporaryOwnerPath,
        ($owner | ConvertTo-Json -Depth 4),
        [System.Text.UTF8Encoding]::new($false))
      Move-Item -LiteralPath $temporaryOwnerPath -Destination $ownerPath -Force

      return [pscustomobject]@{
        Stream = $stream
        InvocationId = $invocationId
        LockPath = $lockPath
        OwnerPath = $ownerPath
      }
    }
    catch [System.IO.IOException] {
      if ($null -ne $stream) {
        $metadataException = $_.Exception
        try {
          if ($temporaryOwnerPath -and (Test-Path -LiteralPath $temporaryOwnerPath)) {
            Remove-Item -LiteralPath $temporaryOwnerPath -Force
          }
        }
        catch {
          # The metadata file is diagnostic only; releasing the exclusive
          # workspace lock is the required cleanup on this failure path.
        }
        finally {
          $stream.Dispose()
        }

        throw [System.IO.IOException]::new(
          "Could not publish workspace build lock metadata '$ownerPath'.",
          $metadataException)
      }

      if (-not $reportedWaiting) {
        Write-Host "Waiting for workspace build lock '$lockPath'."
        $reportedWaiting = $true
      }

      if ($stopwatch.Elapsed -ge $nextReport) {
        $ownerText = $null
        try {
          if (Test-Path -LiteralPath $ownerPath) {
            $ownerText = Get-Content -LiteralPath $ownerPath -Raw
          }
        }
        catch {
          $ownerText = $null
        }

        if ($ownerText) {
          Write-Host "Workspace build lock owner: $ownerText"
        }
        $nextReport = $stopwatch.Elapsed.Add([TimeSpan]::FromSeconds(30))
      }

      if ($stopwatch.Elapsed -ge $Timeout) {
        throw "Timed out after $($stopwatch.Elapsed) waiting for workspace build lock '$lockPath'."
      }

      Start-Sleep -Milliseconds 250
    }
  }
}

function Exit-WorkspaceBuildLock {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $false)]
    [object]$LockHandle
  )

  if ($null -eq $LockHandle) {
    return
  }

  try {
    try {
      if (Test-Path -LiteralPath $LockHandle.OwnerPath) {
        $owner = Get-Content -LiteralPath $LockHandle.OwnerPath -Raw | ConvertFrom-Json
        if ($owner.invocationId -eq $LockHandle.InvocationId) {
          Remove-Item -LiteralPath $LockHandle.OwnerPath -Force
        }
      }
    }
    catch {
      Write-Warning "Could not clean workspace build lock metadata '$($LockHandle.OwnerPath)': $($_.Exception.Message)"
    }
  }
  finally {
    $LockHandle.Stream.Dispose()
  }
}
