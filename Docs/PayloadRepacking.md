# Payload Repacking

The injected payload must be self-contained because the target process cannot be expected to resolve DeepFlowTest's managed dependencies. The `Compile` target uses ILRepack to merge the payload and its approved dependency list.

Outputs are staged under `output/payloads/`:

```text
output/payloads/
  netframework/DeepFlowTest.dll
  netcoreapp/DeepFlowTest.dll
  dotnet/DeepFlowTest.dll
```

Dependency lists are generated under `output/repack/`. `.build/PayloadRepack.proj` receives the primary assembly, dependency-list file, output file, and any framework-specific reference-library path required by ILRepack.

## Policy

Every managed dependency loaded by the payload must be merged, provided by the target runtime, or rejected during packaging validation. No dependency has an accepted exemption from this rule.

After changing payload code or dependency versions, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 Compile
powershell -ExecutionPolicy Bypass -File .\build.ps1 TestIntegration
```

Verify all three payload folders and both native injector architectures before publishing.

