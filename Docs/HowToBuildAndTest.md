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
powershell -ExecutionPolicy Bypass -File .\build.ps1 TestFull
powershell -ExecutionPolicy Bypass -File .\build.ps1 PublishCli --configuration Release
powershell -ExecutionPolicy Bypass -File .\build.ps1 Pack --configuration Release
```

`Compile` builds both native injector architectures, managed projects, and repacked payloads. `TestFast` runs the core and CLI unit tests. `TestIntegration` launches the desktop harness and exercises real-process attachment. `TestFull` combines the declared test lanes.

## Fast iteration

Use `fastbuild.ps1` and `fasttest.ps1` while changing one managed project:

```powershell
.\fastbuild.ps1 core
.\fasttest.ps1 core -Filter TargetActionCommandTests
.\fastbuild.ps1 cli
.\fasttest.ps1 cli
```

The fast scripts do not replace `Compile` when an integration test needs newly repacked payload assemblies.

## Test recordings

Integration tests produce semantic recordings by default. Pass `--no-test-recordings` to the root build, or `-NoTestRecordings` to `fasttest.ps1`, when recordings are intentionally disabled. The build maps this option to the NUnit run parameter `DeepFlowTestTestRecordings`.

## Packaging Workflow

Run `Compile`, `PublishCli`, and `Pack` from a clean checkout. Inspect the staged payloads, native injector resources, published CLI, and NuGet package before release. See `PayloadRepacking.md` for payload rules.

