#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $Model = "gpt-5.6-luna",

    [ValidateSet("none", "low", "medium", "high", "xhigh", "max")]
    [string] $ReasoningEffort = "medium",

    [string] $Configuration = "Debug",

    [string[]] $ScenarioIds = @("wpf-controls", "wpf-navigation", "winforms-controls", "screenshots"),

    [string] $OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\agent-e2e-suites"),

    [string] $CodexPath,

    [string] $CodexPackageVersion = "0.147.0",

    [ValidateRange(30, 3600)]
    [int] $AgentTimeoutSeconds = 900,

    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$scenarioRoot = Join-Path $PSScriptRoot "AgentE2E\Scenarios"
$suiteId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss-fff", [Globalization.CultureInfo]::InvariantCulture)
$suiteDirectory = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) $suiteId
[System.IO.Directory]::CreateDirectory($suiteDirectory) | Out-Null

if (-not $SkipBuild) {
    $payloadProject = Join-Path $repositoryRoot "DeepFlowTest.Payload\DeepFlowTest.Payload.csproj"
    & dotnet msbuild $payloadProject /t:RepackPayloads /p:Configuration=$Configuration /p:RootBuild=true /nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Payload repack failed with exit code $LASTEXITCODE."
    }
    foreach ($project in @(
        "DeepFlowTest.Mcp.Tests\DeepFlowTest.Mcp.Tests.csproj",
        "TestHarnesses\BasicTestHarness\BasicTestHarness.csproj",
        "TestHarnesses\WinFormsExampleApp\WinFormsExampleApp.csproj"
    )) {
        & dotnet build (Join-Path $repositoryRoot $project) --nologo --configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed for $project with exit code $LASTEXITCODE."
        }
    }
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($scenarioId in $ScenarioIds) {
    $scenarioFile = Join-Path $scenarioRoot "$scenarioId.json"
    if (-not (Test-Path -LiteralPath $scenarioFile -PathType Leaf)) {
        throw "Scenario file was not found: $scenarioFile"
    }

    $scenarioOutput = Join-Path $suiteDirectory "runs"
    Write-Host "Starting scenario: $scenarioId"
    $failureMessage = $null
    try {
        $arguments = @{
            ScenarioFile = $scenarioFile
            Model = $Model
            ReasoningEffort = $ReasoningEffort
            Configuration = $Configuration
            OutputDirectory = $scenarioOutput
            CodexPackageVersion = $CodexPackageVersion
            AgentTimeoutSeconds = $AgentTimeoutSeconds
            SkipBuild = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($CodexPath)) {
            $arguments.CodexPath = $CodexPath
        }
        & (Join-Path $PSScriptRoot "Run-CodexMcpAgentE2E.ps1") @arguments
    }
    catch {
        $failureMessage = $_.Exception.Message
    }

    $scenarioRunRoot = Join-Path $scenarioOutput $scenarioId
    $latestReport = Get-ChildItem -LiteralPath $scenarioRunRoot -Filter "run-report.json" -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestReport) {
        $results.Add([pscustomobject]@{
            scenarioId = $scenarioId
            passed = $false
            reportPath = $null
            failure = $failureMessage ?? "The scenario did not produce a report."
        })
        continue
    }

    $scenarioReport = Get-Content -LiteralPath $latestReport.FullName -Raw | ConvertFrom-Json
    Write-Host "Finished scenario: $scenarioId (passed=$($scenarioReport.passed), calls=$($scenarioReport.mcp.toolCalls), failures=$($scenarioReport.mcp.toolFailures))"
    $results.Add([pscustomobject]@{
        scenarioId = $scenarioId
        passed = [bool] $scenarioReport.passed
        elapsedMilliseconds = [long] $scenarioReport.elapsedMilliseconds
        toolCalls = [int] $scenarioReport.mcp.toolCalls
        toolFailures = [int] $scenarioReport.mcp.toolFailures
        reportPath = $latestReport.FullName
        failure = $failureMessage
    })
}

$finishedAt = [DateTimeOffset]::UtcNow
$suiteReportPath = Join-Path $suiteDirectory "suite-report.json"
$suiteReport = [ordered]@{
    suiteId = $suiteId
    model = $Model
    reasoningEffort = $ReasoningEffort
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
    scenarioCount = $results.Count
    passedCount = @($results | Where-Object passed).Count
    failedCount = @($results | Where-Object { -not $_.passed }).Count
    finishedAtUtc = $finishedAt
    scenarios = @($results)
}
[System.IO.File]::WriteAllText($suiteReportPath, ($suiteReport | ConvertTo-Json -Depth 10))

Write-Host "Codex/MCP E2E suite: $suiteId"
Write-Host "Passed: $($suiteReport.passed) ($($suiteReport.passedCount)/$($suiteReport.scenarioCount) scenarios)"
Write-Host "Report: $suiteReportPath"
if (-not $suiteReport.passed) {
    throw "One or more Codex/MCP E2E scenarios failed. See $suiteReportPath"
}
