# Codex MCP Agent End-to-End Tests

Run the full scenario suite from the repository root:

```powershell
pwsh .\Tools\Run-CodexMcpAgentSuite.ps1
```

The suite builds the required projects and runs isolated WPF controls, WPF navigation, WinForms controls, and screenshot scenarios. Each scenario starts the MCP server on a dynamic loopback port, launches a fresh desktop process, and invokes Codex non-interactively with `gpt-5.6-luna`. Codex receives only the run-scoped Streamable HTTP MCP configuration and a structured output schema. See [COVERAGE.md](COVERAGE.md) for the inventory.

For a quick smoke run, use `pwsh .\Tools\Run-CodexMcpAgentE2E.ps1`. After Codex finishes, each scenario independently reads expected target state through the CLI before shutting down every process.

## Results

Suite reports are written below `artifacts\agent-e2e-suites\<suite-id>`. Individual smoke runs default to `artifacts\agent-e2e\<scenario-id>\<run-id>`:

- `run-report.json`: authoritative pass/fail summary and metrics.
- `codex-events.jsonl`: complete non-interactive Codex event stream.
- `codex-final.json`: schema-constrained agent result.
- `mcp-activity.jsonl`: ordered MCP server and tool activity.
- `oracle-*.json`: independent UI state verification.
- `*.stderr.log` and `*.stdout.log`: process diagnostics.

A run fails for a timeout, nonzero Codex exit, failed agent result, any failed MCP call, unexpected shell execution, missing required MCP operations, MCP/Codex call-count disagreement, or failed independent UI verification. Reading a required Codex skill file is recorded separately from unexpected shell use.

## Options

The runner uses `@openai/codex@0.147.0` through `npx` by default because older installed clients may not support GPT-5.6 Luna. Override the package or use a specific installation:

```powershell
pwsh .\Tools\Run-CodexMcpAgentE2E.ps1 `
  -CodexPackageVersion 0.147.0 `
  -Model gpt-5.6-luna `
  -ReasoningEffort medium `
  -AgentTimeoutSeconds 900 `
  -SkipBuild

pwsh .\Tools\Run-CodexMcpAgentE2E.ps1 `
  -CodexPath C:\path\to\codex.ps1
```

Add scenarios as a JSON manifest and prompt in `Tools\AgentE2E\Scenarios`. Use `-ScenarioIds` on the suite runner to select a subset, or `-ScenarioFile` on the single-scenario runner.
