# Codex MCP Agent End-to-End Test

Run from the repository root:

```powershell
pwsh .\Tools\Run-CodexMcpAgentE2E.ps1
```

The runner builds the required projects, starts the MCP server on a dynamic loopback port, launches a fresh HelloWorld desktop process, and invokes Codex non-interactively with `gpt-5.6-luna`. Codex receives only the run-scoped Streamable HTTP MCP configuration and a structured output schema.

The default scenario attaches to the known HelloWorld PID, observes the UI, replaces the `TextBox1` value, clicks `HelloWorldButton`, verifies both resulting values, captures a screenshot, diagnoses target responsiveness, and closes the context. After Codex finishes, the runner independently reads the target through the CLI before shutting down every process.

## Results

Each run writes to `artifacts\agent-e2e\<timestamp>`:

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

Use `-PromptFile` and `-ResultSchemaFile` together when adding another scenario. Update the independent oracle logic if the new scenario expects different application state.
