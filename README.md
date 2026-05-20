# DeepFlowTest

DeepFlowTest is a UI automation framework for WPF and WinForms applications.
It injects a lightweight payload into the target application so tests can query
and interact with the visual tree directly.

See `HowToWriteTests.md` for API examples and `Docs/HowToBuildAndTest.md` for
local build and test commands.

## Optional Semantic Recordings for Tests

Some integration tests can write semantic recording JSONL files while they run.
This is off by default so normal test runs do not create extra files or start
recording streams.

Set `DEEPFLOWTEST_RECORD_TESTS` to `1`, `true`, `yes`, or `on` to enable test
recordings. Leave it unset, set it to an empty value, or use a value such as
`0`, `false`, `no`, or `off` to keep recordings disabled.

By default, recordings are written under the NUnit work directory in
`semantic-recordings`. Set `DEEPFLOWTEST_TEST_RECORDINGS_DIR` to choose a
different directory.

```powershell
$env:DEEPFLOWTEST_RECORD_TESTS = 'true'
$env:DEEPFLOWTEST_TEST_RECORDINGS_DIR = "$PWD\artifacts\test-recordings"
dotnet test .\DeepFlowTest.Tests\DeepFlowTest.Tests.csproj --filter "FullyQualifiedName~RunningProcessAttachIntegrationTests"

# Turn it back off for later commands.
Remove-Item Env:\DEEPFLOWTEST_RECORD_TESTS -ErrorAction SilentlyContinue
Remove-Item Env:\DEEPFLOWTEST_TEST_RECORDINGS_DIR -ErrorAction SilentlyContinue
```

Each generated `.jsonl` file contains a `recording-started` frame, the initial
visual tree snapshot, and later UI deltas or recorded input actions. Semantic
recordings are compact by default for both `StartSemanticRecording(...)` and
env-var test recordings: missing-property entries, empty values, layout-only
nodes, framework/runtime internals, child ID lists, HWNDs, and default
`enabled: true` / `visible: true` state are omitted.
