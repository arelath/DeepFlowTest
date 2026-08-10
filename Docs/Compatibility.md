# Compatibility

DeepFlowTest supports WPF and WinForms applications on .NET Framework and modern .NET. The payload is selected by runtime family and injected into the target process with matching x86 or x64 native resources.

## Runtime payloads

The build produces three payload families under `output/payloads/`:

- `netframework` for .NET Framework applications.
- `netcoreapp` for .NET Core 3.1 applications.
- `dotnet` for .NET 5 and later Windows applications.

Injector binaries and configuration files are staged under `DeepFlowTestResources/x86` and `DeepFlowTestResources/x64`.

## API and protocol evolution

Existing public types remain in the `DeepFlowTest` namespace. Protocol version 1 uses stable command names, property names, status values, and error codes from `ProtocolConstants`.

A new command is additive: older clients can continue using the commands they understand, while a target that does not recognize a newer command returns `unsupported-command`. Changes to existing request or response fields should remain backward compatible whenever possible.

## Framework boundaries

WPF popups, menus, context menus, and secondary presentation sources are exposed as one logical automation tree even though WPF may implement them with additional HWNDs. WinForms secondary forms and modal dialogs are likewise included. Native windows that cannot be represented as framework controls are exposed through native-window or UI Automation adapters.

