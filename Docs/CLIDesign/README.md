# CLI Design

`DeepFlowTest.Cli` is a non-interactive frontend to the payload protocol. It writes compact JSON envelopes to stdout by default and diagnostics to stderr. `--pretty` formats JSON for people, while `--format text` is available for commands with stable text output.

## Command groups

- Configuration: `config get`, `config set`, `config clear`, `config reset`.
- Discovery: `processes`, `ping`, `pipe status`, `tree`, `find`, `node`, `props`, `selectors`.
- Actions: `click`, `drag`, `focus`, `type`, `key`, `set`, `raise`, `invoke`.
- Capture and waiting: `screenshot`, `wait`, `record`, and `stream`.
- Product information: `version`.

Commands that address an application accept a PID, process name, or window-title selector. Element commands accept one target ID or one property-based selector.

## Defaults and safety

`config get` shows the effective defaults. Command-line values override persisted defaults, which override built-in defaults.

Set `DEEPFLOWTEST_CLI_STRICT_ACTIONS=1` in scripts that should deny mutations by default. With strict actions enabled, mutating commands require `--allow-actions`; arbitrary code invocation additionally requires `--allow-arbitrary-invoke`.

Exit codes and JSON error codes are stable so scripts do not need to parse human-readable messages.

