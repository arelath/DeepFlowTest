[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $AgentRunnerPath,

    [Parameter(Mandatory = $true)]
    [string] $Model,

    [string] $Configuration = "Debug",

    [string] $OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\agent-benchmark")
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$cliPath = Join-Path $repositoryRoot "artifacts\bin\DeepFlowTest.Cli\$Configuration\net8.0-windows\DeepFlowTest.Cli.exe"
$mcpPath = Join-Path $repositoryRoot "artifacts\bin\DeepFlowTest.Mcp\$Configuration\net8.0-windows\DeepFlowTest.Mcp.exe"
$targetPath = Join-Path $repositoryRoot "artifacts\bin\HelloWorld\$Configuration\net8.0-windows\HelloWorld.exe"

$requiredBinaries = @($cliPath, $mcpPath, $targetPath)
foreach ($requiredBinary in $requiredBinaries) {
    if (-not (Test-Path -LiteralPath $requiredBinary -PathType Leaf)) {
        throw "Required benchmark binary was not found: $requiredBinary. Build DeepFlowTest.Mcp.Tests first."
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$scenarios = @(
    @{ id = "enter-and-submit"; task = "Enter Paul into the name field and submit." },
    @{ id = "popup-checked"; task = "Open the popup and verify it is checked." },
    @{ id = "disambiguate-save"; task = "Click the Save button in the document toolbar, not the template panel." },
    @{ id = "wait-disappearance"; task = "Wait for the progress dialog to disappear." },
    @{ id = "stale-repair"; task = "Continue after the target control is recreated." },
    @{ id = "binding-diagnosis"; task = "Diagnose why the displayed value is not updating." },
    @{ id = "drag-item"; task = "Drag item A into group B." },
    @{ id = "capture-error"; task = "Capture the error dialog and explain it." }
)

$results = [System.Collections.Generic.List[object]]::new()
foreach ($scenario in $scenarios) {
    foreach ($transport in @("cli", "mcp")) {
        $resultPath = Join-Path $outputRoot "$($scenario.id)-$transport.json"
        & $AgentRunnerPath `
            --model $Model `
            --transport $transport `
            --task-id $scenario.id `
            --task $scenario.task `
            --cli-path $cliPath `
            --mcp-server-path $mcpPath `
            --target-path $targetPath `
            --output $resultPath
        if ($LASTEXITCODE -ne 0) {
            throw "Agent runner failed for '$($scenario.id)' over '$transport' with exit code $LASTEXITCODE."
        }
        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
            throw "Agent runner did not write its result: $resultPath"
        }

        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        foreach ($requiredMetric in @(
            "taskSuccess", "incorrectMutations", "modelTurns", "toolCalls",
            "invalidArgumentCalls", "ambiguousSelectors", "successfulStaleRepairs",
            "tokensReturned", "fullTreeReads", "elapsedMilliseconds")) {
            if ($null -eq $result.$requiredMetric) {
                throw "Result '$resultPath' is missing required metric '$requiredMetric'."
            }
        }

        $results.Add([pscustomobject]@{
            taskId = $scenario.id
            task = $scenario.task
            transport = $transport
            model = $Model
            taskSuccess = [bool]$result.taskSuccess
            incorrectMutations = [int]$result.incorrectMutations
            modelTurns = [int]$result.modelTurns
            toolCalls = [int]$result.toolCalls
            invalidArgumentCalls = [int]$result.invalidArgumentCalls
            ambiguousSelectors = [int]$result.ambiguousSelectors
            successfulStaleRepairs = [int]$result.successfulStaleRepairs
            tokensReturned = [long]$result.tokensReturned
            fullTreeReads = [int]$result.fullTreeReads
            elapsedMilliseconds = [long]$result.elapsedMilliseconds
        })
    }
}

$summary = $results |
    Group-Object transport |
    ForEach-Object {
        $items = @($_.Group)
        [pscustomobject]@{
            transport = $_.Name
            model = $Model
            scenarioCount = $items.Count
            successfulTasks = @($items | Where-Object taskSuccess).Count
            incorrectMutations = ($items | Measure-Object incorrectMutations -Sum).Sum
            modelTurns = ($items | Measure-Object modelTurns -Sum).Sum
            toolCalls = ($items | Measure-Object toolCalls -Sum).Sum
            invalidArgumentCalls = ($items | Measure-Object invalidArgumentCalls -Sum).Sum
            ambiguousSelectors = ($items | Measure-Object ambiguousSelectors -Sum).Sum
            successfulStaleRepairs = ($items | Measure-Object successfulStaleRepairs -Sum).Sum
            tokensReturned = ($items | Measure-Object tokensReturned -Sum).Sum
            fullTreeReads = ($items | Measure-Object fullTreeReads -Sum).Sum
            elapsedMilliseconds = ($items | Measure-Object elapsedMilliseconds -Sum).Sum
        }
    }

$report = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow
    model = $Model
    summary = @($summary)
    results = @($results)
}
$reportPath = Join-Path $outputRoot "parity-report.json"
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
$summary | Format-Table -AutoSize
Write-Host "Detailed parity report: $reportPath"
