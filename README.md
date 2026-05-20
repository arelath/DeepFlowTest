# DeepFlowTest

DeepFlowTest is a UI automation framework for WPF and WinForms applications.
It injects a lightweight payload into the target application so tests can query
and interact with the visual tree directly.

See `HowToWriteTests.md` for API examples and `Docs/HowToBuildAndTest.md` for
local build and test commands.

## Semantic Recordings for Tests

`AppDriver` writes semantic recording JSON files automatically while it is
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

Each generated `.json` file is a JSON array containing a `recording-started` frame, the initial
visual tree snapshot, and later UI deltas or recorded input actions. Semantic
recordings are compact by default for both `StartSemanticRecording(...)` and
test recordings: missing-property entries, empty values, layout-only nodes,
framework/runtime internals, child ID lists, HWNDs, and default `enabled: true`
/ `visible: true` state are omitted. Snapshot nodes are nested under `children`,
and changed delta nodes report only their changed properties under `changes`.
