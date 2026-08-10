# Win32 Dialog Support

DeepFlowTest discovers framework dialogs and native Win32 dialog windows owned by the target process. This includes common file dialogs, message boxes, and WPF modal windows.

## Finding a dialog

Wait for the Dialog or one of its controls by type, automation ID, name, or text. Native controls are read through UI Automation when no WPF or WinForms object is available.

For a file dialog, set the edit control's `FileName` value and then invoke the confirmation action. Prefer stable automation properties over captions because captions vary by Windows version and language.

## Completing a dialog

The known operations `AcceptDialog` and `CancelDialog` map to the natural affirmative and cancellation behavior of the resolved window. In C# these are available as `Element.AcceptDialog()` and `Element.CancelDialog()`.

Modal detection is installed independently from optional synthetic-input hooks so a blocking `ShowDialog` call can be reported without stalling payload shutdown.

