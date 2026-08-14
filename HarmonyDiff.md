# Harmony Diff

Comparison date: 2026-05-13

Compared:
- DeepFlowTest: `DeepFlowTest/AppDriverPayload/AppHooks.cs`, `DeepFlowTest/AppDriverPayload/Patching/*`, `.build/Build.cs`, payload command integration.
- WpfPilot2: `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppHooks.cs`, `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppDriverPayload.cs`, payload command integration.

## Short Answer

Yes, there are differences.

The actual Harmony patch target set is effectively the same in both projects, but the injection/loading strategy, patch activation model, diagnostics, defensive reflection behavior, and command integration are different.

## Same Patch Targets

Both projects patch the same WPF/dialog surfaces:

| Target | DeepFlowTest | WpfPilot2 | Notes |
| --- | --- | --- | --- |
| `MouseDevice.GetButtonState` | Yes | Yes | DeepFlowTest fakes left/right/middle mouse pressed state; WpfPilot2 fakes left/right. |
| `ButtonBase.UpdateIsPressed` | Yes | Yes | Toggles button pressed state without real mouse coordinates. |
| `GridViewColumnHeader.IsMouseOutside` | Yes | Yes | Forces mouse-inside behavior. |
| `MenuItem.HandleMouseDown` | Yes | Yes | Clicks menu headers on mouse down. |
| `MenuItem.HandleMouseUp` | Yes | Yes | Clicks menu items on mouse up. |
| `MenuItem.UpdateIsPressed` | Yes | Yes | Keeps menu pressed state aligned with fake mouse state. |
| `RibbonMenuItem.UpdateIsPressed` | Yes | Yes | Same pressed-state workaround for ribbon menu items. |
| `DataGridCheckBoxColumn.IsMouseOver` | Yes | Yes | Forces checkbox-column hit testing to succeed. |
| `Window.ShowDialog` | Yes | Yes | Sets a modal-dialog flag. |
| `Microsoft.Win32.CommonDialog.ShowDialog` | Yes | Yes | Patches public overloads with zero or one parameter. |
| `System.Windows.Forms.CommonDialog.ShowDialog` | Yes | Yes | Patches public overloads with zero or one parameter. |

Source refs:
- DeepFlowTest `AppHooks.cs`: patch declarations around lines 222, 243, 254, 264, 282, 301, 319, 329, 339, and 349.
- WpfPilot2 `AppHooks.cs`: patch declarations around lines 64, 86, 103, 114, 136, 162, 186, 200, 210, and 221.

## Dependency Loading And Injection

### WpfPilot2

WpfPilot2 loads Harmony as a loose dependency inside the injected payload. `AppDriverPayload.LoadDependencies(...)` explicitly calls:

```text
Load("0Harmony.dll")
```

It also installs an `AssemblyResolve` handler that resolves loose dependency DLLs from the payload DLL directory.

Source refs:
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppDriverPayload.cs:435`
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppDriverPayload.cs:481`
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppDriverPayload.cs:482`

### DeepFlowTest

DeepFlowTest does not have an equivalent payload-side `LoadDependencies(...)` or explicit `Load("0Harmony.dll")` call in `AppDriverPayload`.

Instead, the payload project marks Harmony as an internalized payload dependency. `Shared/DeepFlowTest.PayloadRepack.targets` consumes that MSBuild metadata and writes payloads under `artifacts/staging/payloads/<family>/DeepFlowTest.dll`.

Source refs:
- `DeepFlowTest.Payload/DeepFlowTest.Payload.csproj`
- `Shared/DeepFlowTest.PayloadRepack.targets`

Impact:
- WpfPilot2 expects `0Harmony.dll` to be present beside the injected payload.
- DeepFlowTest expects the generated/repacked payload assembly to carry Harmony internally.
- This is a real deployment difference, even though both projects reference the same package version.

## Package Version

Both projects use `Lib.Harmony` version `2.3.5`.

Source refs:
- DeepFlowTest `Directory.Packages.props`
- WpfPilot2 `D:/dev/research/WpfPilot2/Directory.packages.props`

## Patch Activation Model

### WpfPilot2

WpfPilot2 applies all Harmony patches from the `AppHooks` static constructor:

```text
new Harmony("com.wpfpilot.apphooks.patch").PatchAll(Assembly.GetExecutingAssembly())
```

`EnsureHooked()` is intentionally empty; calling it triggers the static constructor. WpfPilot2 calls `AppHooks.EnsureHooked()` from command processing after building a tree service and only when WPF or WinForms targets are present.

Source refs:
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppHooks.cs:23`
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppHooks.cs:55`
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppHooks.cs:59`
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppDriverPayload.cs:163`
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppDriverPayload.cs:164`

### DeepFlowTest

DeepFlowTest applies Harmony through a runtime patch coordinator:

```text
AppDriverPayload.Start -> AppHooks.Apply(...) -> RuntimeWpfPatchCoordinator.ApplyCurrentRuntime(...)
```

`AppHooks.EnsureHooked()` does the actual `PatchAll(...)`, guarded by a lock and a `hooksApplied` flag:

```text
new Harmony("com.deepflowtest.apphooks.patch").PatchAll(Assembly.GetExecutingAssembly())
```

DeepFlowTest calls `AppHooks.Apply(...)` during payload startup and logs a `WpfPatchResult` summary. It also has a best-effort `TargetActionCommand.TryEnsureAppHooks()` before WPF clicks.

Source refs:
- `DeepFlowTest/AppDriverPayload/AppDriverPayload.cs:32`
- `DeepFlowTest/AppDriverPayload/AppDriverPayload.cs:33`
- `DeepFlowTest/AppDriverPayload/AppHooks.cs:32`
- `DeepFlowTest/AppDriverPayload/AppHooks.cs:40`
- `DeepFlowTest/AppDriverPayload/AppHooks.cs:48`
- `DeepFlowTest/AppDriverPayload/Commands/TargetActionCommand.cs:488`
- `DeepFlowTest/AppDriverPayload/Commands/TargetActionCommand.cs:492`

Impact:
- WpfPilot2 is mostly lazy: patches are first applied when command processing sees supported targets.
- DeepFlowTest is mostly startup-driven: it attempts patch coordination during payload startup.
- DeepFlowTest records diagnostics; WpfPilot2 does not.

## Optional Patch Diagnostics Are Not One-To-One With Harmony Targets

DeepFlowTest has a `DefaultWpfPatchCatalog` that checks optional private members:

| Diagnostic patch name | Checked member |
| --- | --- |
| `MouseButtonState` | `System.Windows.Input.MouseDevice._mouseButtonState` |
| `ButtonPressedState` | `System.Windows.Controls.Primitives.ButtonBase.SetIsPressed` |
| `WpfDialogOwner` | `System.Windows.Window.ShowDialog` |
| `WinFormsDialogOwner` | `System.Windows.Forms.Form.ShowDialog` |
| `ModernMenuMode` | `System.Windows.Controls.MenuItem.IgnoreNextLeftRelease` on modern .NET only |

Source refs:
- `DeepFlowTest/AppDriverPayload/Patching/DefaultWpfPatchers.cs:42`
- `DeepFlowTest/AppDriverPayload/Patching/DefaultWpfPatchers.cs:44`
- `DeepFlowTest/AppDriverPayload/Patching/DefaultWpfPatchers.cs:50`

Important difference:
- Each optional patch's `Apply` delegate is just `AppHooks.EnsureHooked`.
- `EnsureHooked` runs Harmony `PatchAll(...)`, which applies all Harmony patches in the assembly.
- Therefore the optional patch catalog is a diagnostic/availability gate, not an individual per-Harmony-method patch list.

Consequences:
- DeepFlowTest can report several optional patch names as "applied" even though Harmony was only applied once.
- DeepFlowTest does not individually validate every actual Harmony target. For example, there are no separate optional entries for `GridViewColumnHeader.IsMouseOutside`, `MenuItem.HandleMouseDown`, `MenuItem.HandleMouseUp`, `RibbonMenuItem.UpdateIsPressed`, `DataGridCheckBoxColumn.IsMouseOver`, or `Microsoft.Win32.CommonDialog.ShowDialog`.
- WpfPilot2 has no equivalent diagnostics, but its behavior is simpler: the static constructor warms and patches everything in one path.

## Warmup And Reflection Differences

### WpfPilot2

WpfPilot2 warms the hooked methods in the `AppHooks` static constructor using `ReflectionUtility.InvokeOn(...)` and direct reflection calls. The warmup path is direct and assumes the expected private methods/properties exist.

Source refs:
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppHooks.cs:23`
- `D:/dev/research/WpfPilot2/WpfPilot/AppDriverPayload/AppHooks.cs:55`

### DeepFlowTest

DeepFlowTest moves warmup into `WarmupHookedMembers()` and wraps each warmup call in `TryWarmup(...)`, swallowing non-fatal exceptions. It also has local overload selection helpers (`InvokeBestMatch`, `GetCandidateMethods`, `ParametersMatch`) rather than relying on WpfPilot2's `ReflectionUtility`.

Source refs:
- `DeepFlowTest/AppDriverPayload/AppHooks.cs:47`
- `DeepFlowTest/AppDriverPayload/AppHooks.cs:96`
- `DeepFlowTest/AppDriverPayload/AppHooks.cs:121`

Impact:
- DeepFlowTest is more tolerant of runtime/framework drift during warmup.
- WpfPilot2 fails faster if expected internals are unavailable.

## Patch Body Differences

Most patch bodies are behaviorally equivalent, but DeepFlowTest made several defensive changes:

| Area | WpfPilot2 | DeepFlowTest |
| --- | --- | --- |
| `ButtonBase.UpdateIsPressed` | Directly reads `IsPressed` and invokes `SetIsPressed(true/false)`. | Reads `IsPressed` with fallback `false` and invokes `SetIsPressed(!isPressed)` through local best-match reflection. |
| `MenuItem.HandleMouseDown` | Directly casts `__args[0]` to `MouseButtonEventArgs` and directly reads `Role`. | Checks `__args.Length > 0 && __args[0] is MouseButtonEventArgs`; defaults missing `Role` to `TopLevelItem`. |
| `MenuItem.HandleMouseUp` | Same direct cast/read pattern. | Same defensive arg/type guard and role fallback. |
| `MenuItem.UpdateIsPressed` | Direct property/field access; invokes `ClearValue` when not pressed. | Null-safe property/field lookup; only invokes `ClearValue` if the property key exists. |
| `RibbonMenuItem.UpdateIsPressed` | Explicit if/else set. | Single set using `Mouse.LeftButton == Pressed`. |
| Dialog patches | Same intent. | Same intent, but `Window.ShowDialog` prefix does not take `__instance`. |

Potential behavioral edge:
- In the DeepFlowTest menu down/up patches, if Harmony ever passes unexpected args, DeepFlowTest returns `false` and suppresses the original method without doing the WpfPilot2 behavior. WpfPilot2 would likely throw in that situation. This is probably only relevant under framework signature drift.

## Shared State Differences

### Mouse state

WpfPilot2 exposes mutable fields:

```text
public static bool? IsLeftMousePressed
public static bool? IsRightMousePressed
```

DeepFlowTest exposes private setters and routes changes through `SetButton(...)` / `ResetMouseState()`:

```text
public static bool? IsLeftMousePressed { get; private set; }
public static bool? IsRightMousePressed { get; private set; }
```

Source refs:
- WpfPilot2 `AppHooks.cs:275`
- DeepFlowTest `AppHooks.cs:72`
- DeepFlowTest `AppHooks.cs:74`

Impact:
- WpfPilot2 command code can set mouse state directly. Its `RaiseEventCommand` does this for mouse routed events.
- DeepFlowTest command code cannot set those fields directly. Its WPF click path uses `SetButton(...)`; its generic known/expression routed-event path does not appear to fake mouse pressed state the same way.

Relevant refs:
- WpfPilot2 `RaiseEventCommand.cs:61`
- WpfPilot2 `RaiseEventCommand.cs:65`
- DeepFlowTest `TargetActionCommand.cs:452`
- DeepFlowTest `TargetActionCommand.cs:456`
- DeepFlowTest `TargetActionCommand.cs:624`
- DeepFlowTest `TargetActionCommand.cs:642`

### Mouse-over internals

WpfPilot2 stores non-nullable `FieldInfo` / `MethodInfo` fields and uses them directly:

```text
AppHooks.MouseOverElement.SetValue(...)
AppHooks.WriteElementOverElement.Invoke(...)
```

DeepFlowTest stores nullable values and uses null propagation:

```text
AppHooks.MouseOverElement?.SetValue(...)
AppHooks.WriteElementOverElement?.Invoke(...)
```

Source refs:
- WpfPilot2 `ClickCommand.cs:164`
- WpfPilot2 `ClickCommand.cs:166`
- DeepFlowTest `TargetActionCommand.cs:506`
- DeepFlowTest `TargetActionCommand.cs:508`

Impact:
- DeepFlowTest avoids null-reference failures if WPF internals move.
- WpfPilot2 fails faster if those internals are not found.

## Modal Dialog Integration

Both projects use the `ShowDialogCalled` flag to return pending/native-dialog behavior when modal dialogs block the UI thread.

Differences:
- WpfPilot2 resets `ShowDialogCalled` in the main command processing path after `CheckIfShowDialogCalledOrTimeout(...)`.
- DeepFlowTest resets it in `AppDriverCommandDispatcher.RunUiHandlerWithModalWatchAsync(...)` and uses `WaitForShowDialogAsync(...)`.
- DeepFlowTest's wait method returns `Finished` if no dialog is seen by timeout; WpfPilot2's method returns `Pending` when its loop exits by timeout. In normal command flow, command timeout handling may mask some of this, but the method-level behavior differs.

Source refs:
- WpfPilot2 `AppDriverPayload.cs:154`
- WpfPilot2 `AppDriverPayload.cs:188`
- WpfPilot2 `AppDriverPayload.cs:391`
- DeepFlowTest `AppDriverCommandDispatcher.cs:145`
- DeepFlowTest `AppDriverCommandDispatcher.cs:152`
- DeepFlowTest `AppDriverCommandDispatcher.cs:185`
- DeepFlowTest `AppDriverCommandDispatcher.cs:203`

## Visibility And Test Hooks

WpfPilot2:
- `AppHooks` is `internal`.
- No equivalent `WpfPatchResult`.
- No `ResetForTests()` on `AppHooks`.

DeepFlowTest:
- `AppHooks` is `public`.
- Exposes `LastResult` and `Apply(...)`.
- Has `ResetForTests()` for patch diagnostics and mutable state.

Source refs:
- WpfPilot2 `AppHooks.cs:21`
- DeepFlowTest `AppHooks.cs:16`
- DeepFlowTest `AppHooks.cs:19`
- DeepFlowTest `AppHooks.cs:32`
- DeepFlowTest `AppHooks.cs:88`

## Summary Of Material Differences

1. Same Harmony package version and same declared Harmony patch targets.
2. WpfPilot2 loads `0Harmony.dll` explicitly at injection time; DeepFlowTest repacks/internalizes Harmony into generated payload assemblies.
3. WpfPilot2 applies patches through the `AppHooks` static constructor; DeepFlowTest applies through `AppHooks.Apply(...)` plus a runtime patch coordinator.
4. DeepFlowTest logs patch diagnostics and catches/skips optional patch failures; WpfPilot2 has no equivalent diagnostics.
5. DeepFlowTest's optional patch names are not a one-to-one list of Harmony patch methods; they are availability checks that all call `EnsureHooked()`.
6. DeepFlowTest warmup and patch bodies are more null-safe and framework-drift tolerant.
7. WpfPilot2's command integration directly mutates Harmony mouse state for `RaiseEventCommand`; DeepFlowTest's direct mouse-state integration is visible in the click path, not in generic routed-event paths.
8. DeepFlowTest uses nullable mouse-over reflection handles and best-effort behavior; WpfPilot2 uses direct handles and fails faster.
9. Modal dialog detection uses the same Harmony flag idea but has different dispatcher/checker structure and slightly different timeout-return semantics.

## Bottom Line

DeepFlowTest is not a byte-for-byte port of WpfPilot2's Harmony injection. It preserves the main patch target set, but it has a more defensive, diagnostic, repacked, startup-oriented patching model. The biggest parity questions are not the Harmony patch list itself; they are:

- whether DeepFlowTest's repacked payload deployment is always used in the injection path,
- whether startup-time patching should remain different from WpfPilot2's command-time lazy patching,
- whether the optional patch diagnostics should be aligned with actual Harmony patch targets,
- whether DeepFlowTest's routed-event paths should fake `IsLeftMousePressed` / `IsRightMousePressed` the way WpfPilot2's `RaiseEventCommand` does.
