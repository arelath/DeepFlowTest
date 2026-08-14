# DeepFlowTest

DeepFlowTest is a UI automation framework for WPF and WinForms applications.
It injects a lightweight payload into the target application so tests can query
and interact with the visual tree directly. The same engine is exposed through
three surfaces:

- `DeepFlowTest`: the C# test API for launching or attaching to applications,
  finding elements, sending actions, reading properties, screenshots, semantic
  recordings, binding failure capture, and dialog helpers.
- `DeepFlowTest.Cli`: a non-interactive command line interface for scripts and
  agents.
- `DeepFlowTest.Mcp`: a local HTTP Model Context Protocol server for persistent
  agent sessions.

See `HowToWriteTests.md` for library API examples,
`Docs/CliUsage.md` for CLI examples, `Docs/McpUsage.md` for MCP setup, and
`Docs/HowToBuildAndTest.md` for local build and test commands.

## Build

Native injection requires Visual Studio desktop native build tools. Use the
root build script for the complete local layout because it builds the native
injector, repacks payload assemblies, and stages injector resources:

```powershell
.\build.ps1 Compile
.\build.ps1 TestFast
.\build.ps1 PublishCli --configuration Release
.\build.ps1 Pack
```

For managed-code iteration, use the faster project scripts:

```powershell
.\fastbuild.ps1 cli
.\fasttest.ps1 core -Filter ProductConstantsUseDeepFlowTestNames
```

## CLI

The CLI writes compact JSON response envelopes to stdout by default and sends
diagnostics to stderr. Add `--pretty` while reading by hand, or
`--format text` for commands with stable text output.

Targeted commands accept exactly one target selector unless the selector comes
from CLI defaults:

- `--pid <id>`
- `--process <name>`
- `--window-title <substring>`

Common inspection flow:

```powershell
DeepFlowTest.Cli.exe processes --pretty
DeepFlowTest.Cli.exe ping --pid <pid> --pretty
DeepFlowTest.Cli.exe tree --pid <pid> --max-depth 4 --pretty
DeepFlowTest.Cli.exe find --pid <pid> --automation-id SubmitButton --pretty
DeepFlowTest.Cli.exe node --pid <pid> --target <target-id> --pretty
DeepFlowTest.Cli.exe selectors --pid <pid> --target <target-id> --pretty
```

Common action flow:

```powershell
DeepFlowTest.Cli.exe click --pid <pid> --target <target-id> --after target --pretty
DeepFlowTest.Cli.exe type --pid <pid> --automation-id SearchBox --value "hello" --clear-first --after target --pretty
DeepFlowTest.Cli.exe key --pid <pid> --keys Ctrl+A --foreground false --pretty
DeepFlowTest.Cli.exe wait --pid <pid> --automation-id SubmitButton --require-enabled --timeout-ms 5000 --pretty
DeepFlowTest.Cli.exe screenshot --pid <pid> --target <target-id> --out capture.png
```

Read commands include `processes`, `ping`, `pipe status`, `tree`, `find`,
`node`, `props`, `selectors`, `screenshot`, and `wait`. Action commands include
`click`, `drag`, `focus`, `type`, `key`, `set`, `raise`, and `invoke`.
Streaming commands include `visual-tree`, `visual-tree-delta`, `screenshot`,
`event-log`, `binding-failures`, and `semantic-recording`.

CLI defaults are editable:

```powershell
DeepFlowTest.Cli.exe config get --pretty
DeepFlowTest.Cli.exe config set common.process MyApp
DeepFlowTest.Cli.exe config set commands.tree.props "[\"Name\",\"Text\"]" --json
DeepFlowTest.Cli.exe config reset --yes
```

For scripted safety, set `DEEPFLOWTEST_CLI_STRICT_ACTIONS=1`. In strict mode,
mutating commands require `--allow-actions`; raw `invoke --code` always
requires `--allow-arbitrary-invoke`.

## Recorder

`DeepFlowTest.Recorder` is a WPF utility for attaching to a running windowed
process and writing a semantic recording. It lists visible processes, lets you
filter/select a target, and writes to `Documents\DeepFlowTestRecordings` by
default.

The recorder uses the same injection and semantic recording pipeline as the
library and CLI. Build through the solution or `.\build.ps1 Compile` so the
output contains `payloads\*` and both `DeepFlowTestResources\x86` and
`DeepFlowTestResources\x64`.

Video recording requires an FFmpeg executable. It is intentionally not bundled
with the core automation package. Install the optional
`DeepFlowTest.Media.FFmpeg` package, or set
`AppDriver.RecordingFfmpegPathOverride` to an externally managed FFmpeg path.

## Semantic Recordings for Tests

`AppDriver` uses `FailureOnly` automatic diagnostics by default. Semantic frames
are kept in a bounded in-memory ring buffer and are written only when the test or
driver is marked as failed. A failure artifact set can include the semantic
trace, final screenshot, a human-readable `final-tree.txt` visual-tree snapshot,
injector and payload logs, protocol diagnostics, and a versioned manifest. Each
final-state capture is best effort, so an unavailable target does not mask the
original test failure. The default artifact sink reads NUnit context and xUnit
v3's ambient `TestContext`, including the real test name and failure state. It
also adds artifacts directly to xUnit v3 results. xUnit v2 does not expose an
ambient result context, so the explicit capture API below supplies the real test
name and failed state to the default sink for shared xUnit v2 fixtures. A test
suite can alternatively configure its own
`AutomaticDiagnosticsOptions.ArtifactSink`.

Select a different lifecycle with `AppDriverOptions.AutomaticDiagnostics`:

```csharp
var options = new AppDriverOptions
{
    AutomaticDiagnostics = new AutomaticDiagnosticsOptions
    {
        Mode = AutomaticDiagnosticsMode.Always, // FailureOnly, Always, Manual, or Off
        MaximumArtifactSizeBytes = 32 * 1024 * 1024,
        RetentionPolicy = DiagnosticsRetentionPolicy.KeepAll,
    },
};
```

Use `driver.MarkDiagnosticsFailure(exception)` for failures raised outside a
DeepFlowTest command or assertion. If the driver outlives an individual test,
call `driver.CaptureFailureDiagnostics(exception, testName)` before resetting or
shutting down the target. It writes and attaches the screenshot and visual tree
immediately, uses the label as the manifest test name, and prevents teardown from
retrying final-state capture against a dead target. Repeated calls use numbered
artifact names so a shared driver can preserve more than one failed test.
Automatic recording and artifact errors are available through
`driver.Diagnostics`; they never make `AppDriver.Dispose()` throw. An explicitly
started semantic recorder can use `CompleteAsync()` when its recording failures
should be reported to the caller, while its `Dispose()` also remains failure-safe.
If the target has already exited during automatic-diagnostics teardown, the
recorder treats that as the expected end of the stream and skips the remote stop
request while still closing its local reader and flushing buffered frames.

The default command timeout can be adjusted on an active driver. The next
command, selector wait, binding-failure checkpoint, or newly started stream uses
the updated value:

```csharp
driver.Options.Timeout = TimeSpan.FromSeconds(30);
```

Assignments are validated immediately; zero, negative, and unsupported timeout
values are rejected without changing the current timeout.

For this repo's integration lane, use:

```powershell
dotnet test .\DeepFlowTest.Tests\DeepFlowTest.Tests.csproj --filter "FullyQualifiedName~RunningProcessAttachIntegrationTests"
.\build.ps1 TestIntegration --no-test-recordings
.\fasttest.ps1 core -Filter "FullyQualifiedName~RunningProcessAttachIntegrationTests" -NoTestRecordings
```

Each generated `.dft.txt` file uses the condensed agent format: a line-oriented
recording with short target IDs, user actions, selector hints, the initial
visual tree snapshot, and later UI deltas. Missing-property entries, empty
values, layout-only nodes, framework/runtime internals, child ID lists, HWNDs,
and default visible/enabled state are omitted so the trace stays compact. Set
`SemanticRecordingOptions.OutputFormat = SemanticRecordingOutputFormat.CompactJson`
or use `record semantic --recording-format compact-json` when JSON is needed.
See [CondensedSemanticTextFormat.md](CondensedSemanticTextFormat.md) for the
condensed text format details.

## MCP Agent Output

`DeepFlowTest.Mcp` exposes the compact agent profile by default:
`deepflow_open_context`, `deepflow_observe`, `deepflow_find`, `deepflow_act`,
`deepflow_wait`, `deepflow_capture`, `deepflow_diagnose`, and
`deepflow_close_context`. Stateful calls require the `contextId` returned by
`deepflow_open_context`. Selectors and actions are nested typed objects,
ambiguous selectors fail unless first-match behavior is explicitly requested,
and `deepflow_find` returns stable handles that `deepflow_act` can repair after
controls are recreated. Successful calls provide typed structured content;
recoverable execution failures set MCP `isError: true`. Screenshots are returned
as native MCP image content and linked to immutable context-qualified resources.

`deepflow_observe` uses the condensed semantic format by default and applies
semantic pruning. Structured element records are opt-in with `includeElements`;
use `deepflow_find` when only particular controls are needed. Pass
`format: "json"` with a small `limit` when a client needs explicit tree nodes.
Start the server with `--tool-profile full` to additionally expose the granular
legacy tools and streams.

Inputs use discriminated objects. For example, open with
`target: { "mode": "attach", "processId": 1234 }`, find with
`target: { "kind": "semantic", "automationId": "SaveButton" }`, and act with
`action: { "kind": "click", "button": "left", "count": 1 }`. Observation,
screenshot, and diagnostic results link to immutable resources under
`deepflow://contexts/{contextId}/...`.

The server is read-only by default. Start it with `--allow-launch` to let tools
start a process, `--allow-actions` to permit mutating UI tools, and
`--allow-file-writes` when screenshot tools should write to disk. See
`Docs/McpUsage.md` for HTTP client configuration and sample workflows.

For comprehensive real-agent validation, run `Tools/Run-CodexMcpAgentSuite.ps1`.
It runs isolated WPF control/navigation, standalone and hosted WinForms, and
PNG/JPEG/BMP screenshot scenarios with Codex using `gpt-5.6-luna`, independently
verifies durable UI state, enforces non-image MCP payload budgets, shuts down
all processes, and writes aggregate and per-scenario reports under
`artifacts/agent-e2e-suites`. Use
`Tools/Run-CodexMcpAgentE2E.ps1` for the smaller smoke scenario.

For model-level CLI/MCP comparison, run
`Tools/Run-AgentParityBenchmark.ps1` with the same model-backed agent runner and
model name. The harness runs the shared task catalog over both transports and
aggregates success, incorrect mutations, turns, tool calls, invalid arguments,
ambiguities, stale repairs, returned tokens, full-tree reads, and elapsed time.
