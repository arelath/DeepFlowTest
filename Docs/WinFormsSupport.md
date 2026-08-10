# WinForms Support

DeepFlowTest supports WinForms controls alongside WPF controls and native HWND targets. The same element selectors and action APIs are used across framework families.

## Tree discovery

The tree includes the main form, owned secondary forms, controls, menu items, context menus, and visible modal dialogs. Target IDs remain stable while the underlying object is alive; callers should reacquire an element after receiving `stale-target`.

## Actions

Common controls support click, focus, text entry, key input, property changes, and screenshots. WinForms menu and button actions use their framework semantics so event handlers and command state behave as they do for a user.

Native modal dialogs opened by a WinForms application are represented through the native adapter and can be completed with `AcceptDialog` or `CancelDialog` when supported.

