# Recording

DeepFlowTest can Record semantic UI activity from the C# API, CLI, and desktop recorder. Recordings combine an initial visual-tree snapshot with user actions and later tree deltas.

## Semantic recording

`AppDriver` enables Semantic recording by default while attached. Files are written to a `semantic-recordings` directory beside the test assembly unless `SemanticRecordingOptions.OutputDirectory` overrides it. Set `AppDriverOptions.AutoSemanticRecordingEnabled` to `false` to opt out.

The default `dft-condensed/1` format is line-oriented and designed for test diagnostics and agents. `compact-json` and `raw-json` are available when a structured representation is required. See `CondensedSemanticTextFormat.md` in the repository root for the grammar.

CLI examples:

```powershell
DeepFlowTest.Cli.exe record semantic --pid 1234 --out run.dft.txt
DeepFlowTest.Cli.exe stream semantic-recording --pid 1234 --format text
```

## Screenshot streaming

Screenshot streaming periodically emits encoded image frames through the same subscription protocol:

```powershell
DeepFlowTest.Cli.exe stream screenshot --pid 1234 --interval-ms 500
```

Use a bounded interval and duration in unattended jobs. Binary image data is carried in the screenshot response contract as Base64 with width, height, format, and byte-count metadata.

