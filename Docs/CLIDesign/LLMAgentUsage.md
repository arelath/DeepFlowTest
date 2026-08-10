# CLI Usage for LLM Agents

Agents should prefer a read-plan-act-observe loop and retain target selectors in addition to transient target IDs.

1. Run `processes`, `ping`, and `tree` to establish the target and current UI state.
2. Use `find` or `selectors` to choose one unambiguous element.
3. Perform one action and request `--after target` or a tree delta.
4. Re-read state before the next mutation.

## Stable recovery

A `stale-target` response means that the framework object was replaced or collected. Repeat the original selector, acquire the new target ID, and retry only when the operation is safe to repeat. Treat `ambiguous-target`, `target-exited`, and timeout responses as distinct conditions.

For ongoing observation, prefer `visual-tree-delta` over repeated complete trees. Semantic recording and condensed tree output remove layout-only nodes while preserving automation IDs, state, text, and selector hints.

## Output handling

The CLI emits JSON envelopes by default. Parse the `ok`, `command`, `result`, and `error` fields instead of matching console prose. JSON envelopes remain the authoritative machine contract even when condensed text is used as context for reasoning.

Enable strict action policy and pass explicit authorization flags only for the operation being performed. Do not enable arbitrary invocation when standard click, key, set, or raise operations are sufficient.

