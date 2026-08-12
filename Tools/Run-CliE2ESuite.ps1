#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $Configuration = "Debug",

    [string[]] $ScenarioIds = @(
        "foundation",
        "wpf-inspection",
        "wpf-actions",
        "wpf-navigation",
        "winforms-controls",
        "screenshots",
        "streams-and-recording"
    ),

    [string] $OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\cli-e2e-suites"),

    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$scenarioRoot = Join-Path $PSScriptRoot "CliE2E\Scenarios"
$suiteId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss-fff", [Globalization.CultureInfo]::InvariantCulture)
$suiteDirectory = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) $suiteId
$runOutputDirectory = Join-Path $suiteDirectory "runs"
[System.IO.Directory]::CreateDirectory($runOutputDirectory) | Out-Null

if (-not $SkipBuild) {
    & dotnet msbuild (Join-Path $repositoryRoot "DeepFlowTest.Payload\DeepFlowTest.Payload.csproj") /t:RepackPayloads /p:Configuration=$Configuration /p:RootBuild=true /nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Payload repack failed with exit code $LASTEXITCODE."
    }
    foreach ($project in @(
        "DeepFlowTest.Cli.Tests\DeepFlowTest.Cli.Tests.csproj",
        "TestHarnesses\HelloWorld\HelloWorld.csproj",
        "TestHarnesses\BasicTestHarness\BasicTestHarness.csproj",
        "TestHarnesses\WinFormsExampleApp\WinFormsExampleApp.csproj"
    )) {
        & dotnet build (Join-Path $repositoryRoot $project) --configuration $Configuration --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed for $project with exit code $LASTEXITCODE."
        }
    }
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($scenarioId in $ScenarioIds) {
    $scenarioFile = Join-Path $scenarioRoot "$scenarioId.json"
    if (-not (Test-Path -LiteralPath $scenarioFile -PathType Leaf)) {
        throw "CLI E2E scenario file was not found: $scenarioFile"
    }

    $failureMessage = $null
    try {
        & (Join-Path $PSScriptRoot "Run-CliE2E.ps1") `
            -ScenarioFile $scenarioFile `
            -Configuration $Configuration `
            -OutputDirectory $runOutputDirectory `
            -SkipBuild
    }
    catch {
        $failureMessage = $_.Exception.Message
    }

    $latestReport = Get-ChildItem -LiteralPath (Join-Path $runOutputDirectory $scenarioId) -Filter "run-report.json" -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestReport) {
        $results.Add([pscustomobject]@{
            scenarioId = $scenarioId
            passed = $false
            stepCount = 0
            stdoutBytes = 0
            stderrBytes = 0
            reportPath = $null
            failure = $failureMessage ?? "The scenario did not produce a report."
        })
        continue
    }

    $scenarioReport = Get-Content -LiteralPath $latestReport.FullName -Raw | ConvertFrom-Json
    $results.Add([pscustomobject]@{
        scenarioId = $scenarioId
        passed = [bool] $scenarioReport.passed
        elapsedMilliseconds = [long] $scenarioReport.elapsedMilliseconds
        stepCount = [int] $scenarioReport.stepCount
        stdoutBytes = [long] $scenarioReport.stdoutBytes
        stderrBytes = [long] $scenarioReport.stderrBytes
        reportPath = $latestReport.FullName
        failure = $failureMessage ?? $scenarioReport.failure
    })
}

$suiteReportPath = Join-Path $suiteDirectory "suite-report.json"
$suiteReport = [ordered]@{
    suiteId = $suiteId
    configuration = $Configuration
    passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
    scenarioCount = $results.Count
    passedCount = @($results | Where-Object passed).Count
    failedCount = @($results | Where-Object { -not $_.passed }).Count
    stepCount = [int] ($results | Measure-Object stepCount -Sum).Sum
    stdoutBytes = [long] ($results | Measure-Object stdoutBytes -Sum).Sum
    stderrBytes = [long] ($results | Measure-Object stderrBytes -Sum).Sum
    finishedAtUtc = [DateTimeOffset]::UtcNow
    scenarios = @($results)
}
[System.IO.File]::WriteAllText($suiteReportPath, ($suiteReport | ConvertTo-Json -Depth 8))

Write-Host "CLI E2E suite: $suiteId"
Write-Host "Passed: $($suiteReport.passed) ($($suiteReport.passedCount)/$($suiteReport.scenarioCount) scenarios, $($suiteReport.stepCount) steps)"
Write-Host "Captured stdout: $($suiteReport.stdoutBytes) bytes"
Write-Host "Report: $suiteReportPath"
if (-not $suiteReport.passed) {
    throw "One or more CLI E2E scenarios failed. See $suiteReportPath"
}
