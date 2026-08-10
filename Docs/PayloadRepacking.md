# Payload Repacking

The injected payload must be self-contained because the target process cannot be expected to resolve DeepFlowTest's managed dependencies. The `Compile` target uses ILRepack to merge the payload and its approved dependency list.

Outputs are staged under `artifacts/staging/payloads/`:

```text
artifacts/staging/payloads/
  netframework/DeepFlowTest.dll
  netcoreapp/DeepFlowTest.dll
  dotnet/DeepFlowTest.dll
```

`Shared/DeepFlowTest.Frameworks.props` declares the supported target frameworks and runtime-family metadata. `DeepFlowTest.Payload.csproj` derives `TargetFrameworks` from that shared declaration and marks the payload dependencies that ILRepack must internalize. `Shared/DeepFlowTest.PayloadRepack.targets` consumes those evaluated MSBuild items directly; there is no C# framework map or generated dependency-list file.

## Policy

Every managed dependency loaded by the payload must be merged, provided by the target runtime, or rejected during packaging validation. No dependency has an accepted exemption from this rule.

After changing payload code or dependency versions, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 Compile
powershell -ExecutionPolicy Bypass -File .\build.ps1 TestIntegration
```

Verify all three payload folders and both native injector architectures before publishing.

