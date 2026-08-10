[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [int]$Pid,

  [string]$CliPath = ".\artifacts\publish\DeepFlowTest.Cli\Release\DeepFlowTest.Cli.exe"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

& $CliPath processes --pretty
& $CliPath ping --pid $Pid --pretty
& $CliPath tree --pid $Pid --max-depth 4 --pretty
& $CliPath find --pid $Pid --type Button --pretty
& $CliPath selectors --pid $Pid --target root --pretty
& $CliPath wait --pid $Pid --type Window --timeout-ms 5000 --pretty
& $CliPath stream event-log --pid $Pid --duration-ms 1000 --pretty
& $CliPath pipe status --pid $Pid --pretty
