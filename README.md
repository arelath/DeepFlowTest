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
- `DeepFlowTest.Mcp`: a stdio Model Context Protocol server for persistent
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

## Semantic Recordings for Tests

`AppDriver` writes condensed semantic recording files automatically while it is
attached to a target. This is on by default so a failing run leaves a readable
UX trace without needing to remember an opt-in switch.

By default, recordings are written under `semantic-recordings` next to the test
assembly. The DeepFlowTest integration test helper overrides that path to use
the NUnit work directory and test name.

Set `AppDriverOptions.AutoSemanticRecordingEnabled = false` when you
intentionally want to turn them off. For this repo's integration lane, use:

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

`DeepFlowTest.Mcp` uses the same condensed agent format by default for LLM-facing
UI context. `deepflow_get_visual_tree` returns a condensed snapshot, mutating
actions return a condensed delta in `after`, and semantic recording stream reads
return condensed text. MCP also applies semantic pruning: layout-only nodes such
as `Border`, `Grid`, `ContentPresenter`, `Rectangle`, and `Canvas` are omitted
unless they have an automation ID. Pass `outputFormat: "json"` to
`deepflow_get_visual_tree` when a client needs the structured JSON tree.

The server is read-only by default. Start it with `--allow-launch` to let tools
start a process, `--allow-actions` to permit mutating UI tools, and
`--allow-file-writes` when screenshot tools should write to disk. See
`Docs/McpUsage.md` for stdio client configuration and sample workflows.
