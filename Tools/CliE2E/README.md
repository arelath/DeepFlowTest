# CLI end-to-end tests

This suite launches real WPF and WinForms applications and drives them exclusively through the packaged `DeepFlowTest.Cli.exe`. It validates response envelopes, exit codes, durable UI state, image signatures, stream lifecycles, and process cleanup.

Run the complete suite from the repository root:

```powershell
pwsh -File .\Tools\Run-CliE2ESuite.ps1 -Configuration Debug
```

Run one scenario while iterating:

```powershell
pwsh -File .\Tools\Run-CliE2E.ps1 `
  -ScenarioFile .\Tools\CliE2E\Scenarios\wpf-actions.json `
  -Configuration Debug `
  -SkipBuild
```

The suite builds and repacks the injected payload before building the CLI and test applications. `-SkipBuild` is safe only after those outputs are current.

Each command gets separate stdout and stderr logs under `artifacts\cli-e2e-suites\<suite-id>\runs`. Scenario and suite reports contain command results and byte counts, not duplicate response bodies. This preserves the raw evidence while keeping reports small enough for agent review.

Every CLI child process receives:

- `DEEPFLOWTEST_CLI_CONFIG_PATH=<run>\cli-defaults.json`, so config tests never touch interactive defaults.
- `DEEPFLOWTEST_CLI_STRICT_ACTIONS=1`, so mutating steps must explicitly pass `--allow-actions`.

The runner always closes the test window and force-terminates the full process tree if graceful shutdown fails.

## Scenario authoring

A scenario defines an optional `targetExecutable` and a list of CLI steps. Values can use `{{CONFIGURATION}}`, `{{REPOSITORY_ROOT}}`, `{{RUN_DIRECTORY}}`, `{{PID}}`, or a token captured by an earlier JSON path.

Steps support expected exit/error codes, JSON-path assertions, case-insensitive output checks, captured values, minimum envelope counts, and file size/signature checks. Keep state assertions in a later read command when possible; this catches persistence bugs that an action's immediate response can hide.

See [COVERAGE.md](COVERAGE.md) for the command and control matrix.
