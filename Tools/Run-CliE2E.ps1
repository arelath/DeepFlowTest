#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ScenarioFile,

    [string] $Configuration = "Debug",

    [string] $OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\cli-e2e"),

    [ValidateRange(5, 300)]
    [int] $StartupTimeoutSeconds = 45,

    [ValidateRange(5, 300)]
    [int] $CommandTimeoutSeconds = 60,

    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Start-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [AllowEmptyCollection()] [string[]] $Arguments = @(),
        [Parameter(Mandatory = $true)] [string] $WorkingDirectory,
        [hashtable] $Environment = @{},
        [switch] $ShowWindow
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = -not $ShowWindow
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    foreach ($entry in $Environment.GetEnumerator()) {
        $startInfo.Environment[[string] $entry.Key] = [string] $entry.Value
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Failed to start process: $FilePath"
    }

    [pscustomobject]@{
        Process = $process
        StandardOutputTask = $process.StandardOutput.ReadToEndAsync()
        StandardErrorTask = $process.StandardError.ReadToEndAsync()
    }
}

function Complete-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)] $Captured,
        [Parameter(Mandatory = $true)] [int] $TimeoutSeconds
    )

    if (-not $Captured.Process.WaitForExit($TimeoutSeconds * 1000)) {
        $Captured.Process.Kill($true)
        [void] $Captured.Process.WaitForExit(10000)
        throw "Process timed out after $TimeoutSeconds seconds: $($Captured.Process.StartInfo.FileName)"
    }

    [pscustomobject]@{
        ExitCode = $Captured.Process.ExitCode
        StandardOutput = $Captured.StandardOutputTask.GetAwaiter().GetResult()
        StandardError = $Captured.StandardErrorTask.GetAwaiter().GetResult()
    }
}

function Stop-TestApplication {
    param($Captured)

    if ($null -eq $Captured) {
        return
    }
    if (-not $Captured.Process.HasExited) {
        try {
            if ($Captured.Process.CloseMainWindow()) {
                [void] $Captured.Process.WaitForExit(3000)
            }
        }
        catch {
            # Fall through to tree termination.
        }
    }
    if (-not $Captured.Process.HasExited) {
        $Captured.Process.Kill($true)
        [void] $Captured.Process.WaitForExit(10000)
    }
    $null = $Captured.StandardOutputTask.GetAwaiter().GetResult()
    $null = $Captured.StandardErrorTask.GetAwaiter().GetResult()
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
        Start-Sleep -Milliseconds 150
    }
    throw "Test application did not expose a responsive main window within $TimeoutSeconds seconds."
}

function Get-JsonPathValue {
    param(
        [Parameter(Mandatory = $true)] $Root,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $current = $Root
    foreach ($segment in $Path.Split('.', [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($null -eq $current) {
            return $null
        }
        if ($segment -match '^\d+$') {
            $current = @($current)[[int] $segment]
        }
        else {
            $property = $current.PSObject.Properties[$segment]
            if ($null -eq $property) {
                return $null
            }
            $current = $property.Value
        }
    }
    return $current
}

function Get-OptionalValues {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    if ($Object.PSObject.Properties.Name -notcontains $Name -or $null -eq $Object.$Name) {
        return @()
    }
    return @($Object.$Name)
}

function Test-FileSignature {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [string] $Format
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    switch ($Format.ToLowerInvariant()) {
        "png" { return $bytes.Length -ge 8 -and $bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50 -and $bytes[2] -eq 0x4e -and $bytes[3] -eq 0x47 }
        "jpg" { return $bytes.Length -ge 3 -and $bytes[0] -eq 0xff -and $bytes[1] -eq 0xd8 -and $bytes[2] -eq 0xff }
        "jpeg" { return $bytes.Length -ge 3 -and $bytes[0] -eq 0xff -and $bytes[1] -eq 0xd8 -and $bytes[2] -eq 0xff }
        "bmp" { return $bytes.Length -ge 2 -and $bytes[0] -eq 0x42 -and $bytes[1] -eq 0x4d }
        "gif" { return $bytes.Length -ge 6 -and [Text.Encoding]::ASCII.GetString($bytes, 0, 6) -in @("GIF87a", "GIF89a") }
        default { throw "Unknown signature format '$Format'." }
    }
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$scenarioPath = [System.IO.Path]::GetFullPath($ScenarioFile)
if (-not (Test-Path -LiteralPath $scenarioPath -PathType Leaf)) {
    throw "CLI E2E scenario was not found: $scenarioPath"
}
$scenario = Get-Content -LiteralPath $scenarioPath -Raw | ConvertFrom-Json
$scenarioId = [string] $scenario.id
if ([string]::IsNullOrWhiteSpace($scenarioId)) {
    throw "CLI E2E scenario must define a non-empty id."
}

$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss-fff", [Globalization.CultureInfo]::InvariantCulture)
$runDirectory = Join-Path ([System.IO.Path]::GetFullPath($OutputDirectory)) (Join-Path $scenarioId $runId)
$logDirectory = Join-Path $runDirectory "commands"
[System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$isolatedConfigPath = Join-Path $runDirectory "cli-defaults.json"

if (-not $SkipBuild) {
    & dotnet msbuild (Join-Path $repositoryRoot "DeepFlowTest.Payload\DeepFlowTest.Payload.csproj") /t:RepackPayloads /p:Configuration=$Configuration /p:RootBuild=true /nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Payload repack failed with exit code $LASTEXITCODE."
    }
    & dotnet build (Join-Path $repositoryRoot "DeepFlowTest.Cli\DeepFlowTest.Cli.csproj") --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "CLI build failed with exit code $LASTEXITCODE."
    }
}

$cliPath = Join-Path $repositoryRoot "artifacts\bin\DeepFlowTest.Cli\$Configuration\net8.0-windows\DeepFlowTest.Cli.exe"
if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf)) {
    throw "CLI executable was not found: $cliPath"
}

$tokens = @{
    "REPOSITORY_ROOT" = $repositoryRoot
    "RUN_DIRECTORY" = $runDirectory
    "CONFIGURATION" = $Configuration
}
function Expand-Tokens {
    param([AllowEmptyString()] [string] $Value)
    $expanded = $Value
    foreach ($entry in $tokens.GetEnumerator()) {
        $expanded = $expanded.Replace("{{$($entry.Key)}}", [string] $entry.Value, [StringComparison]::Ordinal)
    }
    return $expanded
}

$testApplication = $null
$stepResults = [System.Collections.Generic.List[object]]::new()
$startedAt = [DateTimeOffset]::UtcNow
$failure = $null
try {
    if ($scenario.PSObject.Properties.Name -contains "targetExecutable" -and -not [string]::IsNullOrWhiteSpace([string] $scenario.targetExecutable)) {
        $targetPath = Expand-Tokens ([string] $scenario.targetExecutable)
        if (-not [System.IO.Path]::IsPathRooted($targetPath)) {
            $targetPath = Join-Path $repositoryRoot $targetPath
        }
        $targetPath = [System.IO.Path]::GetFullPath($targetPath)
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            throw "Test application was not found: $targetPath"
        }
        $testApplication = Start-CapturedProcess -FilePath $targetPath -WorkingDirectory ([System.IO.Path]::GetDirectoryName($targetPath)) -ShowWindow
        Wait-ForMainWindow -Process $testApplication.Process -TimeoutSeconds $StartupTimeoutSeconds
        $tokens["PID"] = $testApplication.Process.Id.ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    $stepIndex = 0
    foreach ($step in $scenario.steps) {
        $stepIndex++
        $stepName = [string] $step.name
        Write-Host "[$scenarioId] $stepName"
        $arguments = [System.Collections.Generic.List[string]]::new()
        foreach ($argument in $step.arguments) {
            $arguments.Add((Expand-Tokens ([string] $argument)))
        }
        $usesJson = -not ($step.PSObject.Properties.Name -contains "parseJson") -or [bool] $step.parseJson
        if ($usesJson) {
            $arguments.Add("--format")
            $arguments.Add("json")
            $arguments.Add("--hide-empty")
        }
        if ($step.PSObject.Properties.Name -contains "targetBound" -and [bool] $step.targetBound) {
            if (-not $tokens.ContainsKey("PID")) {
                throw "Step '$stepName' is target-bound, but the scenario has no test application."
            }
            $arguments.Add("--pid")
            $arguments.Add($tokens["PID"])
        }

        $stepStarted = [DateTimeOffset]::UtcNow
        $captured = Start-CapturedProcess -FilePath $cliPath -Arguments $arguments.ToArray() -WorkingDirectory $repositoryRoot -Environment @{
            DEEPFLOWTEST_CLI_CONFIG_PATH = $isolatedConfigPath
            DEEPFLOWTEST_CLI_STRICT_ACTIONS = "1"
        }
        $commandResult = Complete-CapturedProcess -Captured $captured -TimeoutSeconds $CommandTimeoutSeconds
        $elapsed = [long] ([DateTimeOffset]::UtcNow - $stepStarted).TotalMilliseconds
        $safeName = ($stepName -replace '[^A-Za-z0-9._-]+', '-').Trim('-')
        $baseName = "{0:D2}-{1}" -f $stepIndex, $safeName
        $stdoutPath = Join-Path $logDirectory "$baseName.stdout.log"
        $stderrPath = Join-Path $logDirectory "$baseName.stderr.log"
        [System.IO.File]::WriteAllText($stdoutPath, $commandResult.StandardOutput)
        [System.IO.File]::WriteAllText($stderrPath, $commandResult.StandardError)

        $expectedExitCode = if ($step.PSObject.Properties.Name -contains "expectedExitCode") { [int] $step.expectedExitCode } else { 0 }
        if ($commandResult.ExitCode -ne $expectedExitCode) {
            throw "Step '$stepName' exited $($commandResult.ExitCode); expected $expectedExitCode. See $stdoutPath"
        }

        $envelopes = @()
        if ($usesJson) {
            foreach ($line in ($commandResult.StandardOutput -split "`r?`n")) {
                if (-not [string]::IsNullOrWhiteSpace($line)) {
                    try { $envelopes += ($line | ConvertFrom-Json) }
                    catch { throw "Step '$stepName' emitted invalid JSON. See $stdoutPath" }
                }
            }
            if ($envelopes.Count -eq 0) {
                throw "Step '$stepName' emitted no JSON envelopes. See $stdoutPath"
            }
            $expectedErrorCode = if ($step.PSObject.Properties.Name -contains "expectedErrorCode") { [string] $step.expectedErrorCode } else { $null }
            $lastEnvelope = $envelopes[-1]
            if ($null -eq $expectedErrorCode) {
                if (@($envelopes | Where-Object { -not $_.ok }).Count -ne 0) {
                    throw "Step '$stepName' emitted a failed envelope. See $stdoutPath"
                }
            }
            elseif ($lastEnvelope.ok -or [string] $lastEnvelope.error.code -ne $expectedErrorCode) {
                throw "Step '$stepName' expected error '$expectedErrorCode'. See $stdoutPath"
            }
            if ($step.PSObject.Properties.Name -contains "minimumEnvelopeCount" -and $envelopes.Count -lt [int] $step.minimumEnvelopeCount) {
                throw "Step '$stepName' emitted $($envelopes.Count) envelopes; expected at least $($step.minimumEnvelopeCount)."
            }
        }

        foreach ($text in (Get-OptionalValues -Object $step -Name "contains")) {
            if (-not $commandResult.StandardOutput.Contains((Expand-Tokens ([string] $text)), [StringComparison]::OrdinalIgnoreCase)) {
                throw "Step '$stepName' did not contain '$text'. See $stdoutPath"
            }
        }
        foreach ($text in (Get-OptionalValues -Object $step -Name "notContains")) {
            if ($commandResult.StandardOutput.Contains((Expand-Tokens ([string] $text)), [StringComparison]::OrdinalIgnoreCase)) {
                throw "Step '$stepName' unexpectedly contained '$text'. See $stdoutPath"
            }
        }
        foreach ($assertion in (Get-OptionalValues -Object $step -Name "assertions")) {
            $actual = Get-JsonPathValue -Root $envelopes[-1] -Path ([string] $assertion.path)
            $expectedValue = if ($assertion.PSObject.Properties.Name -contains "equals") { Expand-Tokens ([string] $assertion.equals) } else { $null }
            if ($null -ne $expectedValue -and [string] $actual -ne $expectedValue) {
                throw "Step '$stepName' expected $($assertion.path)='$($assertion.equals)', received '$actual'."
            }
            if ($assertion.PSObject.Properties.Name -contains "minimum" -and [double] $actual -lt [double] $assertion.minimum) {
                throw "Step '$stepName' expected $($assertion.path) >= $($assertion.minimum), received '$actual'."
            }
            if ($assertion.PSObject.Properties.Name -contains "exists" -and [bool] $assertion.exists -and $null -eq $actual) {
                throw "Step '$stepName' expected JSON path $($assertion.path) to exist."
            }
        }
        foreach ($capture in (Get-OptionalValues -Object $step -Name "captures")) {
            $capturedValue = Get-JsonPathValue -Root $envelopes[-1] -Path ([string] $capture.path)
            if ([string]::IsNullOrWhiteSpace([string] $capturedValue)) {
                throw "Step '$stepName' could not capture $($capture.name) from $($capture.path)."
            }
            $tokens[[string] $capture.name] = [string] $capturedValue
        }
        foreach ($fileAssertion in (Get-OptionalValues -Object $step -Name "files")) {
            $assertedPath = Expand-Tokens ([string] $fileAssertion.path)
            if (-not (Test-Path -LiteralPath $assertedPath -PathType Leaf)) {
                throw "Step '$stepName' did not create expected file: $assertedPath"
            }
            $length = (Get-Item -LiteralPath $assertedPath).Length
            if ($fileAssertion.PSObject.Properties.Name -contains "minimumBytes" -and $length -lt [long] $fileAssertion.minimumBytes) {
                throw "Step '$stepName' created a $length-byte file; expected at least $($fileAssertion.minimumBytes): $assertedPath"
            }
            if ($fileAssertion.PSObject.Properties.Name -contains "signature" -and -not (Test-FileSignature -Path $assertedPath -Format ([string] $fileAssertion.signature))) {
                throw "Step '$stepName' created a file with the wrong $($fileAssertion.signature) signature: $assertedPath"
            }
        }

        $stepResults.Add([pscustomobject]@{
            name = $stepName
            passed = $true
            exitCode = $commandResult.ExitCode
            elapsedMilliseconds = $elapsed
            envelopeCount = $envelopes.Count
            stdoutBytes = [Text.Encoding]::UTF8.GetByteCount($commandResult.StandardOutput)
            stderrBytes = [Text.Encoding]::UTF8.GetByteCount($commandResult.StandardError)
            stdoutPath = $stdoutPath
            stderrPath = $stderrPath
        })
    }
}
catch {
    $failure = $_.Exception.Message
}
finally {
    Stop-TestApplication -Captured $testApplication
}

$finishedAt = [DateTimeOffset]::UtcNow
$reportPath = Join-Path $runDirectory "run-report.json"
$report = [ordered]@{
    scenarioId = $scenarioId
    passed = $null -eq $failure
    startedAtUtc = $startedAt
    finishedAtUtc = $finishedAt
    elapsedMilliseconds = [long] ($finishedAt - $startedAt).TotalMilliseconds
    processId = if ($tokens.ContainsKey("PID")) { [int] $tokens["PID"] } else { $null }
    stepCount = $stepResults.Count
    stdoutBytes = [long] ($stepResults | Measure-Object stdoutBytes -Sum).Sum
    stderrBytes = [long] ($stepResults | Measure-Object stderrBytes -Sum).Sum
    isolatedConfigPath = $isolatedConfigPath
    failure = $failure
    steps = @($stepResults)
}
[System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8))
Write-Host "CLI E2E scenario: $scenarioId (passed=$($report.passed), steps=$($report.stepCount), bytes=$($report.stdoutBytes))"
Write-Host "Report: $reportPath"
if (-not $report.passed) {
    throw $failure
}
