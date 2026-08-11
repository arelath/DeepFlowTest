#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $Model = "gpt-5.6-luna",

    [ValidateSet("none", "low", "medium", "high", "xhigh", "max")]
    [string] $ReasoningEffort = "medium",

    [string] $Configuration = "Debug",

    [string] $PromptFile = (Join-Path $PSScriptRoot "AgentE2E\hello-world.prompt.txt"),

    [string] $ResultSchemaFile = (Join-Path $PSScriptRoot "AgentE2E\codex-agent-result.schema.json"),

    [string] $OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\agent-e2e"),

    [string] $CodexPath,

    [string] $CodexPackageVersion = "0.147.0",

    [ValidateRange(30, 3600)]
    [int] $AgentTimeoutSeconds = 600,

    [ValidateRange(5, 300)]
    [int] $StartupTimeoutSeconds = 45,

    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Start-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [AllowEmptyCollection()] [string[]] $Arguments = @(),
        [Parameter(Mandatory = $true)] [string] $WorkingDirectory,
        [Parameter(Mandatory = $true)] [string] $StandardOutputPath,
        [Parameter(Mandatory = $true)] [string] $StandardErrorPath,
        [string] $StandardInputText
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $StandardInputText
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start process: $FilePath"
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if ($null -ne $StandardInputText) {
        $process.StandardInput.Write($StandardInputText)
        $process.StandardInput.Close()
    }

    [pscustomobject]@{
        Process = $process
        StandardOutputTask = $stdoutTask
        StandardErrorTask = $stderrTask
        StandardOutputPath = $StandardOutputPath
        StandardErrorPath = $StandardErrorPath
        LogsCompleted = $false
    }
}

function Complete-CapturedProcess {
    param([Parameter(Mandatory = $true)] $CapturedProcess)

    if ($CapturedProcess.LogsCompleted) {
        return
    }

    $stdout = $CapturedProcess.StandardOutputTask.GetAwaiter().GetResult()
    $stderr = $CapturedProcess.StandardErrorTask.GetAwaiter().GetResult()
    [System.IO.File]::WriteAllText($CapturedProcess.StandardOutputPath, $stdout)
    [System.IO.File]::WriteAllText($CapturedProcess.StandardErrorPath, $stderr)
    $CapturedProcess.LogsCompleted = $true
}

function Stop-CapturedProcess {
    param([Parameter(Mandatory = $true)] $CapturedProcess)

    $process = $CapturedProcess.Process
    if (-not $process.HasExited) {
        try {
            if ($process.CloseMainWindow()) {
                [void] $process.WaitForExit(5000)
            }
        }
        catch {
            # Fall through to termination below.
        }
    }

    if (-not $process.HasExited) {
        $process.Kill($true)
        [void] $process.WaitForExit(10000)
    }
}

function Wait-ForEndpoint {
    param(
        [Parameter(Mandatory = $true)] [string] $EndpointFile,
        [Parameter(Mandatory = $true)] $ServerProcess,
        [Parameter(Mandatory = $true)] [int] $TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($ServerProcess.HasExited) {
            throw "MCP server exited before publishing its endpoint (exit code $($ServerProcess.ExitCode))."
        }

        if (Test-Path -LiteralPath $EndpointFile -PathType Leaf) {
            try {
                $endpoint = (Get-Content -LiteralPath $EndpointFile -Raw | ConvertFrom-Json).streamableHttpUrl
                if (-not [string]::IsNullOrWhiteSpace($endpoint)) {
                    return [string] $endpoint
                }
            }
            catch {
                # The server may still be replacing the endpoint file.
            }
        }

        Start-Sleep -Milliseconds 200
    }

    throw "MCP server did not publish an endpoint within $TimeoutSeconds seconds."
}

function Wait-ForMainWindow {
    param(
        [Parameter(Mandatory = $true)] $Process,
        [Parameter(Mandatory = $true)] [int] $TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw "Test application exited before its main window became ready (exit code $($Process.ExitCode))."
        }

        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero -and $Process.Responding) {
            return
        }

        Start-Sleep -Milliseconds 200
    }

    throw "Test application did not expose a responsive main window within $TimeoutSeconds seconds."
}

function ConvertTo-TomlString {
    param([Parameter(Mandatory = $true)] [string] $Value)

    '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function Resolve-CodexLauncher {
    param(
        [string] $RequestedPath,
        [Parameter(Mandatory = $true)] [string] $PackageVersion
    )

    $resolved = $RequestedPath
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        $resolved = (Get-Command npx -ErrorAction Stop).Source
        $resolved = [System.IO.Path]::GetFullPath($resolved)
        $extension = [System.IO.Path]::GetExtension($resolved)
        if ([string]::Equals($extension, ".ps1", [StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{
                FilePath = (Join-Path $PSHOME "pwsh.exe")
                PrefixArguments = @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $resolved, "--yes", "@openai/codex@$PackageVersion")
                Description = "@openai/codex@$PackageVersion via npx"
            }
        }
        throw "The default isolated Codex launcher requires npx.ps1. Pass -CodexPath to use a specific Codex installation."
    }
    $resolved = [System.IO.Path]::GetFullPath($resolved)
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Codex launcher was not found: $resolved"
    }

    $extension = [System.IO.Path]::GetExtension($resolved)
    if ([string]::Equals($extension, ".ps1", [StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{
            FilePath = (Join-Path $PSHOME "pwsh.exe")
            PrefixArguments = @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $resolved)
            Description = $resolved
        }
    }

    if ([string]::Equals($extension, ".cmd", [StringComparison]::OrdinalIgnoreCase)) {
        $powerShellLauncher = [System.IO.Path]::ChangeExtension($resolved, ".ps1")
        if (Test-Path -LiteralPath $powerShellLauncher -PathType Leaf) {
            return [pscustomobject]@{
                FilePath = (Join-Path $PSHOME "pwsh.exe")
                PrefixArguments = @("-NoLogo", "-NoProfile", "-NonInteractive", "-File", $powerShellLauncher)
                Description = $powerShellLauncher
            }
        }
        throw "Use the codex.ps1 or native executable launcher for reliable argument handling: $resolved"
    }

    [pscustomobject]@{
        FilePath = $resolved
        PrefixArguments = @()
        Description = $resolved
    }
}

function Invoke-OracleRead {
    param(
        [Parameter(Mandatory = $true)] [string] $CliPath,
        [Parameter(Mandatory = $true)] [int] $ProcessId,
        [Parameter(Mandatory = $true)] [string] $AutomationId,
        [Parameter(Mandatory = $true)] [string] $OutputBasePath,
        [Parameter(Mandatory = $true)] [string] $WorkingDirectory
    )

    $captured = Start-CapturedProcess -FilePath $CliPath -Arguments @(
        "find", "--pid", $ProcessId.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--automation-id", $AutomationId,
        "--include", "properties,path",
        "--props", "Text,Name,Content,AutomationProperties.AutomationId",
        "--limit", "500",
        "--format", "json",
        "--pretty"
    ) -WorkingDirectory $WorkingDirectory -StandardOutputPath "$OutputBasePath.json" -StandardErrorPath "$OutputBasePath.stderr.log"

    if (-not $captured.Process.WaitForExit(90000)) {
        Stop-CapturedProcess $captured
        Complete-CapturedProcess $captured
        throw "Independent oracle read timed out for automation ID '$AutomationId'."
    }
    Complete-CapturedProcess $captured
    [pscustomobject]@{
        ExitCode = $captured.Process.ExitCode
        OutputPath = $captured.StandardOutputPath
        ErrorPath = $captured.StandardErrorPath
        Text = Get-Content -LiteralPath $captured.StandardOutputPath -Raw
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss-fff", [Globalization.CultureInfo]::InvariantCulture)
$runDirectory = Join-Path $outputRoot $runId
[System.IO.Directory]::CreateDirectory($runDirectory) | Out-Null

$mcpPath = Join-Path $repositoryRoot "artifacts\bin\DeepFlowTest.Mcp\$Configuration\net8.0-windows\DeepFlowTest.Mcp.exe"
$cliPath = Join-Path $repositoryRoot "artifacts\bin\DeepFlowTest.Cli\$Configuration\net8.0-windows\DeepFlowTest.Cli.exe"
$targetPath = Join-Path $repositoryRoot "artifacts\bin\HelloWorld\$Configuration\net8.0-windows\HelloWorld.exe"
$endpointFile = Join-Path $runDirectory "mcp-endpoint.json"
$activityLog = Join-Path $runDirectory "mcp-activity.jsonl"
$codexEvents = Join-Path $runDirectory "codex-events.jsonl"
$codexErrors = Join-Path $runDirectory "codex.stderr.log"
$finalMessage = Join-Path $runDirectory "codex-final.json"
$reportPath = Join-Path $runDirectory "run-report.json"
$promptOutput = Join-Path $runDirectory "prompt.txt"
$schemaOutput = Join-Path $runDirectory "result.schema.json"

$server = $null
$target = $null
$codex = $null
$endpoint = $null
$codexExitCode = $null
$codexLauncherDescription = $null
$codexTimedOut = $false
$oracleTextBox = $null
$oracleEventDisplay = $null
$runError = $null
$startedAt = [DateTimeOffset]::UtcNow

try {
    if (-not $SkipBuild) {
        & dotnet build (Join-Path $repositoryRoot "DeepFlowTest.Mcp.Tests\DeepFlowTest.Mcp.Tests.csproj") --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE."
        }
    }

    foreach ($requiredFile in @($mcpPath, $cliPath, $targetPath, $PromptFile, $ResultSchemaFile)) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Required file was not found: $requiredFile"
        }
    }

    Copy-Item -LiteralPath $ResultSchemaFile -Destination $schemaOutput

    $server = Start-CapturedProcess -FilePath $mcpPath -Arguments @(
        "--http-port", "0",
        "--endpoint-file", $endpointFile,
        "--activity-log-file", $activityLog,
        "--activity-retention-limit", "2048",
        "--tool-profile", "agent",
        "--allow-actions",
        "--timeout-ms", "60000",
        "--attach-timeout-ms", "60000",
        "--start-minimized"
    ) -WorkingDirectory ([System.IO.Path]::GetDirectoryName($mcpPath)) -StandardOutputPath (Join-Path $runDirectory "mcp.stdout.log") -StandardErrorPath (Join-Path $runDirectory "mcp.stderr.log")

    $endpoint = Wait-ForEndpoint -EndpointFile $endpointFile -ServerProcess $server.Process -TimeoutSeconds $StartupTimeoutSeconds

    $target = Start-CapturedProcess -FilePath $targetPath -Arguments @() -WorkingDirectory ([System.IO.Path]::GetDirectoryName($targetPath)) -StandardOutputPath (Join-Path $runDirectory "test-app.stdout.log") -StandardErrorPath (Join-Path $runDirectory "test-app.stderr.log")
    Wait-ForMainWindow -Process $target.Process -TimeoutSeconds $StartupTimeoutSeconds

    $prompt = (Get-Content -LiteralPath $PromptFile -Raw).Replace("{{TARGET_PID}}", $target.Process.Id.ToString([Globalization.CultureInfo]::InvariantCulture))
    [System.IO.File]::WriteAllText($promptOutput, $prompt)

    $launcher = Resolve-CodexLauncher -RequestedPath $CodexPath -PackageVersion $CodexPackageVersion
    $codexLauncherDescription = $launcher.Description
    $codexArguments = @($launcher.PrefixArguments) + @(
        "exec",
        "--ignore-user-config",
        "--ephemeral",
        "--skip-git-repo-check",
        "--sandbox", "read-only",
        "--model", $Model,
        "--json",
        "--output-last-message", $finalMessage,
        "--output-schema", $schemaOutput,
        "--cd", $runDirectory,
        "--config", "approval_policy=`"never`"",
        "--config", "model_reasoning_effort=$(ConvertTo-TomlString $ReasoningEffort)",
        "--config", "mcp_servers.deepflow_e2e.url=$(ConvertTo-TomlString $endpoint)",
        "--config", "mcp_servers.deepflow_e2e.required=true",
        "--config", "mcp_servers.deepflow_e2e.startup_timeout_sec=$StartupTimeoutSeconds",
        "--config", "mcp_servers.deepflow_e2e.tool_timeout_sec=90",
        "--config", "mcp_servers.deepflow_e2e.default_tools_approval_mode=`"approve`"",
        "-"
    )

    $codex = Start-CapturedProcess -FilePath $launcher.FilePath -Arguments $codexArguments -WorkingDirectory $runDirectory -StandardOutputPath $codexEvents -StandardErrorPath $codexErrors -StandardInputText $prompt
    if (-not $codex.Process.WaitForExit($AgentTimeoutSeconds * 1000)) {
        $codexTimedOut = $true
        Stop-CapturedProcess $codex
    }
    Complete-CapturedProcess $codex
    $codexExitCode = $codex.Process.ExitCode

    if (-not $target.Process.HasExited) {
        $oracleTextBox = Invoke-OracleRead -CliPath $cliPath -ProcessId $target.Process.Id -AutomationId "TextBox1" -OutputBasePath (Join-Path $runDirectory "oracle-textbox") -WorkingDirectory $repositoryRoot
        $oracleEventDisplay = Invoke-OracleRead -CliPath $cliPath -ProcessId $target.Process.Id -AutomationId "HelloWorldInput" -OutputBasePath (Join-Path $runDirectory "oracle-event-display") -WorkingDirectory $repositoryRoot
    }
}
catch {
    $runError = $_
}
finally {
    foreach ($captured in @($codex, $target, $server)) {
        if ($null -eq $captured) {
            continue
        }
        try {
            Stop-CapturedProcess $captured
            Complete-CapturedProcess $captured
        }
        catch {
            if ($null -eq $runError) {
                $runError = $_
            }
        }
    }
}

$validationFailures = [System.Collections.Generic.List[string]]::new()
if ($null -ne $runError) {
    $validationFailures.Add($runError.Exception.Message)
}
if ($codexTimedOut) {
    $validationFailures.Add("Codex exceeded the $AgentTimeoutSeconds-second timeout.")
}
if ($null -eq $codexExitCode -or $codexExitCode -ne 0) {
    $validationFailures.Add("Codex exit code was $codexExitCode.")
}

$agentResult = $null
if (Test-Path -LiteralPath $finalMessage -PathType Leaf) {
    try {
        $agentResult = Get-Content -LiteralPath $finalMessage -Raw | ConvertFrom-Json
        if (-not $agentResult.success) {
            $validationFailures.Add("The agent reported that the workflow failed.")
        }
    }
    catch {
        $validationFailures.Add("Codex final output was not valid JSON: $($_.Exception.Message)")
    }
}
else {
    $validationFailures.Add("Codex did not write its final structured result.")
}

$activityEvents = @()
if (Test-Path -LiteralPath $activityLog -PathType Leaf) {
    $activityEvents = @(Get-Content -LiteralPath $activityLog | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
}
$toolStarts = @($activityEvents | Where-Object kind -EQ "tool.start")
$toolSuccesses = @($activityEvents | Where-Object kind -EQ "tool.success")
$toolFailures = @($activityEvents | Where-Object kind -EQ "tool.failure")
$successfulToolNames = @($toolSuccesses | ForEach-Object { $_.Name })
$codexEventObjects = @()
if (Test-Path -LiteralPath $codexEvents -PathType Leaf) {
	$codexEventObjects = @(Get-Content -LiteralPath $codexEvents | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_ | ConvertFrom-Json })
}
$commandExecutions = @($codexEventObjects | Where-Object { $_.type -eq "item.completed" -and $_.item.type -eq "command_execution" })
$completedMcpCalls = @($codexEventObjects | Where-Object { $_.type -eq "item.completed" -and $_.item.type -eq "mcp_tool_call" })
$skillLoadPattern = "Get-Content\s+-Raw\s+.+\\+\.codex\\+skills\\+.+\\+SKILL\.md"
$skillLoads = @($commandExecutions | Where-Object { $_.item.command -match $skillLoadPattern })
$unexpectedCommands = @($commandExecutions | Where-Object { $_.item.command -notmatch $skillLoadPattern })
if ($unexpectedCommands.Count -gt 0) {
	$validationFailures.Add("Codex used $($unexpectedCommands.Count) unexpected command execution(s) during the MCP-only evaluation.")
}
if ($toolStarts.Count -eq 0) {
    $validationFailures.Add("The MCP activity log contains no tool calls.")
}
if ($toolFailures.Count -gt 0) {
	$validationFailures.Add("The MCP activity log contains $($toolFailures.Count) failed tool call(s).")
}
foreach ($requiredTool in @("OpenContext", "Observe", "Find", "Act", "Capture", "Diagnose", "CloseContext")) {
    if ($requiredTool -notin $successfulToolNames) {
        $validationFailures.Add("The MCP activity log has no successful $requiredTool call.")
    }
}
if (@($toolSuccesses | Where-Object Name -EQ "Act").Count -lt 2) {
    $validationFailures.Add("Expected at least two successful Act calls (type and click).")
}
if ($toolStarts.Count -ne $completedMcpCalls.Count) {
	$validationFailures.Add("MCP activity counted $($toolStarts.Count) calls, but Codex recorded $($completedMcpCalls.Count) completed MCP calls.")
}

$expectedInput = "MCP agent validation by GPT-5.6 Luna"
$expectedEvent = "HelloWorldButton_Click event triggered."
if ($null -eq $oracleTextBox -or $oracleTextBox.ExitCode -ne 0 -or -not $oracleTextBox.Text.Contains($expectedInput, [StringComparison]::Ordinal)) {
    $validationFailures.Add("Independent CLI verification did not find the expected TextBox1 value.")
}
if ($null -eq $oracleEventDisplay -or $oracleEventDisplay.ExitCode -ne 0 -or -not $oracleEventDisplay.Text.Contains($expectedEvent, [StringComparison]::Ordinal)) {
    $validationFailures.Add("Independent CLI verification did not find the expected event display value.")
}

$finishedAt = [DateTimeOffset]::UtcNow
$report = [ordered]@{
    runId = $runId
    passed = $validationFailures.Count -eq 0
    startedAtUtc = $startedAt
    finishedAtUtc = $finishedAt
    elapsedMilliseconds = [long] ($finishedAt - $startedAt).TotalMilliseconds
    model = $Model
    reasoningEffort = $ReasoningEffort
    codexLauncher = $codexLauncherDescription
    endpoint = $endpoint
    targetProcessId = if ($null -ne $target) { $target.Process.Id } else { $null }
    codexExitCode = $codexExitCode
    codexTimedOut = $codexTimedOut
    agentResult = $agentResult
    mcp = [ordered]@{
        toolCalls = $toolStarts.Count
        toolSuccesses = $toolSuccesses.Count
        toolFailures = $toolFailures.Count
        toolsAttempted = @($toolStarts | ForEach-Object { $_.Name })
        failedTools = @($toolFailures | ForEach-Object { [ordered]@{ name = $_.Name; summary = $_.Summary } })
    }
    codex = [ordered]@{
		completedMcpCalls = $completedMcpCalls.Count
		commandExecutions = $commandExecutions.Count
		requiredSkillLoads = $skillLoads.Count
		unexpectedCommandExecutions = $unexpectedCommands.Count
		commands = @($commandExecutions | ForEach-Object { $_.item.command })
	}
    oracle = [ordered]@{
        textBoxVerified = $null -ne $oracleTextBox -and $oracleTextBox.ExitCode -eq 0 -and $oracleTextBox.Text.Contains($expectedInput, [StringComparison]::Ordinal)
        eventDisplayVerified = $null -ne $oracleEventDisplay -and $oracleEventDisplay.ExitCode -eq 0 -and $oracleEventDisplay.Text.Contains($expectedEvent, [StringComparison]::Ordinal)
    }
    validationFailures = @($validationFailures)
    files = [ordered]@{
        prompt = $promptOutput
        codexEvents = $codexEvents
        codexErrors = $codexErrors
        codexFinal = $finalMessage
        mcpActivity = $activityLog
        mcpStandardOutput = Join-Path $runDirectory "mcp.stdout.log"
        mcpStandardError = Join-Path $runDirectory "mcp.stderr.log"
        oracleTextBox = Join-Path $runDirectory "oracle-textbox.json"
        oracleEventDisplay = Join-Path $runDirectory "oracle-event-display.json"
    }
}
[System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 12))

Write-Host "Codex/MCP agent run: $runId"
Write-Host "Passed: $($report.passed)"
Write-Host "MCP tool calls: $($report.mcp.toolCalls) ($($report.mcp.toolFailures) failed)"
Write-Host "Report: $reportPath"
if (-not $report.passed) {
    foreach ($failure in $validationFailures) {
        Write-Warning $failure
    }
    throw "Codex/MCP agent validation failed. See $reportPath"
}
