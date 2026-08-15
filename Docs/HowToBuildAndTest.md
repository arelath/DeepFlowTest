# How to Build and Test

DeepFlowTest requires a Windows .NET SDK and the Visual Studio Desktop development with C++ build tools. Run commands from the repository root.

## Full build commands

The root build script exposes these targets:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 Restore
powershell -ExecutionPolicy Bypass -File .\build.ps1 Compile
powershell -ExecutionPolicy Bypass -File .\build.ps1 TestFast
powershell -ExecutionPolicy Bypass -File .\build.ps1 CompileTestHarnesses
powershell -ExecutionPolicy Bypass -File .\build.ps1 TestIntegration
powershell -ExecutionPolicy Bypass -File .\build.ps1 TestCliE2E
powershell -ExecutionPolicy Bypass -File .\build.ps1 TestFull
powershell -ExecutionPolicy Bypass -File .\build.ps1 PublishCli --configuration Release
powershell -ExecutionPolicy Bypass -File .\build.ps1 Pack --configuration Release
```

`Compile` builds both native injector architectures, managed projects, and repacked payloads. `TestFast` runs the client, payload, CLI, and MCP unit tests. `TestMcp` runs the MCP suite by itself. `TestIntegration` launches the desktop harness and exercises real-process attachment. `TestCliE2E` runs the packaged CLI against the real WPF and WinForms harnesses and writes command logs under `artifacts/cli-e2e-suites`. `TestFull` combines the declared fast and integration lanes; run the longer CLI E2E lane explicitly on an interactive Windows worker.

## Fast iteration

Use `fastbuild.ps1` and `fasttest.ps1` while changing one managed project:

```powershell
.\fastbuild.ps1 core
.\fasttest.ps1 core -Filter TargetActionCommandTests
.\fasttest.ps1 payload
.\fastbuild.ps1 cli
.\fasttest.ps1 cli
.\fastbuild.ps1 mcp
.\fasttest.ps1 mcp
```

The fast scripts do not replace `Compile` when an integration test needs newly repacked payload assemblies.

## Test recordings

Integration tests produce semantic recordings by default. Pass `--no-test-recordings` to the root build, or `-NoTestRecordings` to `fasttest.ps1`, when recordings are intentionally disabled. The build maps this option to the NUnit run parameter `DeepFlowTestTestRecordings`.

## Packaging Workflow

Run `Compile`, `PublishCli`, and `Pack` from a clean checkout. `Pack` invokes standard SDK `dotnet pack` for `DeepFlowTest.csproj` and the optional `DeepFlowTest.Media.FFmpeg` project; NuGet derives dependency groups from the evaluated project references. Inspect `artifacts/staging`, `artifacts/publish`, and `artifacts/packages/<configuration>` before release. See `PayloadRepacking.md` for payload rules.

All managed projects use project-isolated outputs:

```text
artifacts/bin/<project>/<configuration>/<tfm>/
artifacts/obj/<project>/<configuration>/<tfm>/
artifacts/staging/payloads/<runtime-family>/
artifacts/packages/<configuration>/
```

The core package contains automation payloads and native injectors, but not FFmpeg. Install `DeepFlowTest.Media.FFmpeg` only for video recording, or configure `AppDriver.RecordingFfmpegPathOverride` with a separately managed executable.

