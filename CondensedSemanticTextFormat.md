# Condensed Semantic Text Format

DeepFlowTest writes semantic recordings in a line-oriented text format named
`dft-condensed/1`. It is designed for humans and agents to scan UI behavior
quickly without the noise of the full visual tree JSON.

The same format is used by:

- automatic `.dft.txt` recordings from `AppDriver`
- `SemanticRecordingOutputFormat.CondensedAgent`
- `DeepFlowTest.Cli.exe record semantic` by default
- `DeepFlowTest.Cli.exe stream semantic-recording --format text`
- MCP condensed visual tree, action `after`, and semantic recording outputs

Use `compact-json` or `raw-json` when a tool needs a strict structured data
contract. The condensed text format is versioned and intentionally stable, but
it is optimized for readable traces.

## Header

Every condensed recording starts with a header:

```text
dft-condensed/1 profile=agent source=compact-json
```

`dft-condensed/1` is the format version. `profile` is usually `agent`; the
`condensed-diagnostic` writer uses `profile=diagnostic`. Both profiles currently
share the same line grammar. `source=compact-json` means the text is rendered
from DeepFlowTest's compact semantic frame model.

## Example

```text
dft-condensed/1 profile=agent source=compact-json
@1 started at="2026-05-21T22:08:54.7610000Z" recording=run-1 process=1234
@2 snapshot at="2026-05-21T22:08:55.0120000Z" treeSeq=7 nodes=3/6 omitted=3
Window [0001] #MainWindow root
  TextBox [1388] #SearchBox text="hello"
  Button [1389] #SubmitButton .Save
  Image [271a] source="pack://application:,,,/Assets/toolbar-save.png"
@3 action at="2026-05-21T22:08:55.2680000Z" kind=click
> target Button [1389] #SubmitButton summary="Button[AutomationId='SubmitButton']"
> input mouseButton=left clickCount=1
> selector automation-id property=automationId value="SubmitButton" confidence=0.98 cli="--automation-id \"SubmitButton\""
@4 delta at="2026-05-21T22:08:55.7710000Z" baseTree=7 currentTree=8 added=1 changed=1 removed=1
+ TextBlock [1390] #StatusText text="Saved"
* Button [1389] #SubmitButton !enabled
- [1388]
```

## Frame Lines

Each frame starts with:

```text
@<sequence> <kind> at="<utc timestamp>" <fields...>
```

Known frame kinds are:

- `started`: recording metadata. The source frame kind is
  `recording-started`, but the text line shortens it to `started`.
- `snapshot`: a complete compact visual tree snapshot.
- `action`: a user input action captured by semantic recording.
- `delta`: visual tree changes since a previous snapshot.

If input actions overflow the recorder queue, the writer emits:

```text
! droppedActions=<count>
```

## Snapshot Frames

A snapshot header includes:

- `treeSeq`: visual tree sequence number.
- `nodes=<included>/<source>`: included semantic nodes over source nodes.
- `omitted`: source nodes omitted from the compact output.
- `truncated=true` and `reason=<text>` when the source snapshot hit a limit.

Node lines follow the snapshot header. Child nodes are indented by two spaces:

```text
Window [0001] #MainWindow root
  Button [1389] #SubmitButton .Save !enabled
```

The node shape is:

```text
<TypeName> [<short-id>] <identity tokens> <state tokens> <other key=value tokens>
```

Target IDs are shortened to the suffix after the last dash. For example,
`dft-target-1389` prints as `[1389]`. Short IDs are intended for trace reading
and same-recording context, not as durable IDs across sessions.

## Action Frames

An action frame names the action kind on the frame header:

```text
@3 action at="2026-05-21T22:08:55.2680000Z" kind=click
```

Optional detail lines can follow:

- `> target`: target type, short ID, identity/state tokens, and optional
  `summary`.
- `> input`: action-specific input such as `mouseButton`, `clickCount`, `text`,
  or `keys`.
- `> selector`: selector hints. Common fields are selector kind, `property`,
  `value`, `confidence`, and `cli`.

Example:

```text
> target ToggleButton [3e] #SaveButton !checked summary="ToggleButton[AutomationId='SaveButton']"
> input mouseButton=left clickCount=1
> selector automation-id property=automationId value="SaveButton" confidence=0.98
```

## Delta Frames

A delta header includes:

- `baseTree`: source tree sequence number.
- `currentTree`: destination tree sequence number.
- `added`, `changed`, `removed`: counts of nodes printed by the condensed
  delta.
- `addedOmitted`, `changedOmitted`, `removedPruned`: optional nonzero counts
  for source changes omitted by semantic filtering or pruning.

Delta body lines use prefixes:

- `+`: added node. Added children keep the `+` prefix and are indented.
- `*`: changed node. The line includes identity tokens plus changed properties.
- `-`: removed short IDs. Removed lines print at most 20 IDs and then
  `removedOmitted=<count>` when more were omitted.

Example:

```text
@4 delta at="2026-05-21T22:08:55.7710000Z" baseTree=7 currentTree=8 added=1 changed=2 removed=1
+ TextBlock [1390] #StatusText text="Saved"
* Button [1389] #SubmitButton !enabled
* CheckBox [1391] #Advanced checked
- [1388]
```

## Tokens

Identity properties are printed first. The most compact forms are:

- `#SaveButton`: `automationId=SaveButton`
- `.Save`: `name=Save`
- `autoName="Save"`: automation name
- `text="Saved"`, `content="..."`, `header="..."`, `title="..."`, `uid="..."`
- `source="..."`: source identity used as a fallback for an image without an
  automation ID or name

Boolean state properties use shorthand:

- `checked`: state is true.
- `!checked`: state is false.

State tokens include `root`, `visible`, `enabled`, `checked`, `expanded`,
`open`, `selected`, `submenuOpen`, and `visibility`.

Other values are printed as `key=value`. Strings are bare only when they contain
letters, digits, `_`, `-`, `.`, `:`, or `/`. Other strings are JSON quoted.
Numbers use invariant culture, booleans use `true` or `false`, null uses
`null`, and arrays or objects are rendered as compact JSON.

## Semantic Filtering

Condensed output omits noisy data before rendering:

- empty values and property extraction errors
- default `visible=true`, `enabled=true`, and `visibility=Visible`
- non-semantic source properties that are not part of the compact property set
- child ID lists, HWNDs, framework/runtime internals, and similar transport
  details from the raw frame model
- nodes without a useful identity, notable state, or window/dialog role

Default snapshots capture `Source` for WPF `Image` and `ImageSource` entries
only when they have no automation ID or name. This keeps named images compact,
keeps visible unnamed images in the diagnostic tree, and provides a value that
can be used with
`ElementSelector.ByType("Image").WithProperty(KnownProperties.Source, value)`.

MCP condensed output also enables structural pruning. In that mode, layout-only
`Border`, `Grid`, `ContentPresenter`, `Rectangle`, and `Canvas` nodes are
omitted unless they have an automation ID. Normal CLI and library condensed
recordings do not enable that extra pruning by default, so structural nodes with
useful identity can still appear.

## CLI Formats

`record semantic` accepts these recording formats:

- `condensed-agent`, plus aliases `agent` and `text`
- `condensed-diagnostic`, plus alias `diagnostic`
- `compact-json`, plus alias `json`
- `raw-json`

The default is `condensed-agent`, and its default file extension is `.dft.txt`.
JSON formats use `.json` by convention.
