# DeepFlowTest

DeepFlowTest is a UI automation framework for WPF and WinForms applications.
It injects a lightweight payload into the target application so tests can query
and interact with the visual tree directly.

See `HowToWriteTests.md` for API examples and `Docs/HowToBuildAndTest.md` for
local build and test commands.

## Semantic Recordings for Tests

WPF attach integration tests write semantic recording JSONL files while they
run. This is on by default so a failing run leaves a readable UX trace without
needing to remember an opt-in switch.

By default, recordings are written under the NUnit work directory in
`semantic-recordings`.

Use the CLI switch when you intentionally want to turn them off:

```powershell
dotnet test .\DeepFlowTest.Tests\DeepFlowTest.Tests.csproj --filter "FullyQualifiedName~RunningProcessAttachIntegrationTests"
.\build.ps1 TestIntegration --no-test-recordings
.\fasttest.ps1 core -Filter "FullyQualifiedName~RunningProcessAttachIntegrationTests" -NoTestRecordings
```

Each generated `.jsonl` file contains a `recording-started` frame, the initial
visual tree snapshot, and later UI deltas or recorded input actions. Semantic
recordings are compact by default for both `StartSemanticRecording(...)` and
test recordings: missing-property entries, empty values, layout-only nodes,
framework/runtime internals, child ID lists, HWNDs, and default `enabled: true`
/ `visible: true` state are omitted.
