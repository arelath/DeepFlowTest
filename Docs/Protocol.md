# Payload Protocol

DeepFlowTest communicates with its in-process payload over a versioned named pipe. Each request names a command and carries command-specific fields. Responses use stable success, status, error-code, and correlation metadata.

## Commands and errors

Protocol command names and error codes are defined in `DeepFlowTest.Contracts.ProtocolConstants`. Common errors include `invalid-arguments`, `unsupported-command`, `unsupported-target`, `stale-target`, `command-timeout`, and `target-exited`.

Target IDs identify live objects, not permanent selectors. A client should repeat its selector when a command returns `stale-target` and then retry only the safe operation it intended.

## Streaming

`StartSendingCommand` creates a subscription and `StopSendingCommand` closes it. Every `StreamMessage` includes a monotonically increasing `SequenceNumber`, timestamp, stream kind, and payload. Consumers use sequence gaps and dropped-count metadata to detect backpressure loss.

Supported stream kinds include:

- `visual-tree`
- `visual-tree-delta`
- `screenshot`
- `event-log`
- `binding-failures`
- `semantic-recording`

Messages and JSON envelopes are additive contracts: readers should ignore fields they do not understand and preserve stable error handling.

