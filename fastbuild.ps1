[CmdletBinding()]
param(
  [Parameter(Position = 0)]
  [string]$Target = "cli",

  [string]$Configuration = "Debug",

  [string]$Framework,

  [switch]$Restore,

  [switch]$NoDependencies,

  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$DotNetArguments
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$root = Split-Path $MyInvocation.MyCommand.Path -Parent
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { "dotnet" }

function Resolve-Target([string]$name) {
  $targets = @{
    "library" = @{ Project = "DeepFlowTest\DeepFlowTest.csproj"; Framework = "net5.0-windows" }
    "core" = @{ Project = "DeepFlowTest\DeepFlowTest.csproj"; Framework = "net5.0-windows" }
    "cli" = @{ Project = "DeepFlowTest.Cli\DeepFlowTest.Cli.csproj"; Framework = "net8.0-windows" }
    "core-tests" = @{ Project = "DeepFlowTest.Tests\DeepFlowTest.Tests.csproj"; Framework = "net8.0-windows" }
    "cli-tests" = @{ Project = "DeepFlowTest.Cli.Tests\DeepFlowTest.Cli.Tests.csproj"; Framework = "net8.0-windows" }
    "hello" = @{ Project = "TestHarnesses\HelloWorld\HelloWorld.csproj"; Framework = "net8.0-windows" }
    "basic" = @{ Project = "TestHarnesses\BasicTestHarness\BasicTestHarness.csproj"; Framework = "net8.0-windows" }
  }

  if ($targets.ContainsKey($name)) {
    return $targets[$name]
  }

  return @{ Project = $name; Framework = "" }
}

$resolved = Resolve-Target $Target
$project = [string]$resolved.Project
$defaultFramework = [string]$resolved.Framework
$targetFramework = if ($Framework) { $Framework } else { $defaultFramework }
$projectPath = if ([System.IO.Path]::IsPathRooted($project)) { $project } else { Join-Path $root $project }

if (-not (Test-Path -LiteralPath $projectPath)) {
  throw "Build target '$Target' resolved to '$projectPath', but that file does not exist."
}

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
$assetsCandidates = @(
  (Join-Path $root "output\obj\$projectName\project.assets.json"),
  (Join-Path $root "TestHarnesses\output\obj\$projectName\project.assets.json")
)
$hasAssets = $assetsCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

$arguments = @("build", $projectPath, "--configuration", $Configuration, "-nologo", "-clp:NoSummary")
if ($targetFramework) {
  $arguments += @("--framework", $targetFramework)
}
if (-not $Restore -and $hasAssets) {
  $arguments += "--no-restore"
}
if ($NoDependencies) {
  $arguments += "--no-dependencies"
}
if ($DotNetArguments) {
  $arguments += $DotNetArguments
}

Push-Location $root
try {
  & $dotnet @arguments
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
  Pop-Location
}
