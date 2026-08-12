# Payload Protocol

DeepFlowTest communicates with its in-process payload over a versioned named pipe. Each request names a command and carries command-specific fields. Responses use stable success, status, error-code, and correlation metadata.

## Commands and errors

Protocol command names and error codes are defined in `DeepFlowTest.Contracts.ProtocolConstants`. Common errors include `invalid-arguments`, `unsupported-command`, `unsupported-target`, `stale-target`, `command-timeout`, and `target-exited`.

Target IDs identify live objects, not permanent selectors. A client should repeat its selector when a command returns `stale-target` and then retry only the safe operation it intended.

## Connections

Reusable payload sessions use one persistent, serialized control connection. A client sends one framed command, reads its framed response, and only then sends the next command on that connection. `HelloCommandResponse.ControlConnectionMode` advertises `persistent-serialized`, and `ConnectionId` identifies the server-side connection. Clients negotiate with `HelloCommand` before retaining the connection and fall back to one connection per command when a payload advertises `one-shot` or omits the capability. `PipeStatusCommandResponse.ActiveConnectionCount` and the `activeConnections` counter expose connection health.

Streams use separate connections so an idle control connection never blocks frame delivery or stream lifecycle operations. Full request multiplexing is deliberately unsupported: clients must serialize control exchanges, and they must not retry a request after an ambiguous mid-response disconnect. The next command can reconnect safely.

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

