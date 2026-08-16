[CmdletBinding()]
param(
  [TimeSpan]$WorkspaceLockTimeout = [TimeSpan]::FromMinutes(10),

  [Parameter(Position = 0, Mandatory = $false, ValueFromRemainingArguments = $true)]
  [string[]]$BuildArguments
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"

$root = Split-Path $MyInvocation.MyCommand.Path -Parent
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { "dotnet" }
$buildProject = Join-Path $root ".build\_build.csproj"
$lockHelper = Join-Path $root "Tools\WorkspaceBuildLock.ps1"
. $lockHelper
$workspaceLock = $null

Push-Location $root
try {
  $workspaceLock = Enter-WorkspaceBuildLock -Root $root -Timeout $WorkspaceLockTimeout -CommandDescription "build.ps1 $($BuildArguments -join ' ')"
  & $dotnet build $buildProject -nologo -clp:NoSummary --verbosity quiet
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

  & $dotnet run --project $buildProject --no-build -- $BuildArguments
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
  Exit-WorkspaceBuildLock $workspaceLock
  Pop-Location
}
