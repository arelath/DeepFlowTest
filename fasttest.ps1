[CmdletBinding()]
param(
  [Parameter(Position = 0)]
  [string]$Target = "core-tests",

  [string]$Configuration = "Debug",

  [string]$Framework,

  [string]$Filter,

  [switch]$Restore,

  [switch]$NoBuild,

  [switch]$NoTestRecordings,

  [TimeSpan]$WorkspaceLockTimeout = [TimeSpan]::FromMinutes(10),

  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$DotNetArguments
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$root = Split-Path $MyInvocation.MyCommand.Path -Parent
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { "dotnet" }
$lockHelper = Join-Path $root "Tools\WorkspaceBuildLock.ps1"
. $lockHelper
$workspaceLock = $null

function Resolve-TestTarget([string]$name) {
  $targets = @{
    "core" = @{ Project = "DeepFlowTest.Tests\DeepFlowTest.Tests.csproj"; Framework = "net8.0-windows" }
    "core-tests" = @{ Project = "DeepFlowTest.Tests\DeepFlowTest.Tests.csproj"; Framework = "net8.0-windows" }
    "payload" = @{ Project = "DeepFlowTest.Payload.Tests\DeepFlowTest.Payload.Tests.csproj"; Framework = "net8.0-windows" }
    "payload-tests" = @{ Project = "DeepFlowTest.Payload.Tests\DeepFlowTest.Payload.Tests.csproj"; Framework = "net8.0-windows" }
    "cli" = @{ Project = "DeepFlowTest.Cli.Tests\DeepFlowTest.Cli.Tests.csproj"; Framework = "net8.0-windows" }
    "cli-tests" = @{ Project = "DeepFlowTest.Cli.Tests\DeepFlowTest.Cli.Tests.csproj"; Framework = "net8.0-windows" }
    "mcp" = @{ Project = "DeepFlowTest.Mcp.Tests\DeepFlowTest.Mcp.Tests.csproj"; Framework = "net8.0-windows" }
    "mcp-tests" = @{ Project = "DeepFlowTest.Mcp.Tests\DeepFlowTest.Mcp.Tests.csproj"; Framework = "net8.0-windows" }
  }

  if ($targets.ContainsKey($name)) {
    return $targets[$name]
  }

  return @{ Project = $name; Framework = "" }
}

$resolved = Resolve-TestTarget $Target
$project = [string]$resolved.Project
$defaultFramework = [string]$resolved.Framework
$targetFramework = if ($Framework) { $Framework } else { $defaultFramework }
$projectPath = if ([System.IO.Path]::IsPathRooted($project)) { $project } else { Join-Path $root $project }

if (-not (Test-Path -LiteralPath $projectPath)) {
  throw "Test target '$Target' resolved to '$projectPath', but that file does not exist."
}

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
$assets = Join-Path $root "output\obj\$projectName\project.assets.json"

$arguments = @("test", $projectPath, "--configuration", $Configuration, "-nologo", "--logger", "console;verbosity=minimal")
if ($targetFramework) {
  $arguments += @("--framework", $targetFramework)
}
if (-not $Restore -and (Test-Path -LiteralPath $assets)) {
  $arguments += "--no-restore"
}
if ($NoBuild) {
  $arguments += "--no-build"
}
if ($Filter) {
  $arguments += @("--filter", $Filter)
}
if ($DotNetArguments) {
  $arguments += $DotNetArguments
}
if ($NoTestRecordings) {
  $arguments += @("--", 'TestRunParameters.Parameter(name="DeepFlowTestTestRecordings",value="off")')
}

Push-Location $root
try {
  $workspaceLock = Enter-WorkspaceBuildLock -Root $root -Timeout $WorkspaceLockTimeout -CommandDescription "fasttest.ps1 $Target"
  & $dotnet @arguments
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
  Exit-WorkspaceBuildLock $workspaceLock
  Pop-Location
}
