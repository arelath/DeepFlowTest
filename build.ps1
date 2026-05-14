[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $false, ValueFromRemainingArguments = $true)]
  [string[]]$BuildArguments
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"

$root = Split-Path $MyInvocation.MyCommand.Path -Parent
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { "dotnet" }
$buildProject = Join-Path $root ".build\_build.csproj"

Push-Location $root
try {
  & $dotnet build $buildProject -nologo -clp:NoSummary --verbosity quiet
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

  & $dotnet run --project $buildProject --no-build -- $BuildArguments
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
  Pop-Location
}
