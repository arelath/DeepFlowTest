# Codex MCP agent E2E coverage

The suite launches a fresh MCP server and target application for each scenario, runs Codex with GPT-5.6 Luna, applies independent CLI assertions, stops every process, and aggregates the per-run activity logs.

| Scenario | Framework and controls | MCP behavior |
| --- | --- | --- |
| `wpf-controls` | WPF Window, TextBox, TextBlock, Button, CheckBox, ToggleButton, Popup, ListBox/ListBoxItem, Expander, ScrollViewer, Border drag targets; hosted WinForms Panel, TextBox, CheckBox, Button | attach, condensed observe, typed semantic find, handles, type/click/invoke/drag actions, action verification, visible/exists/stable waits, diagnose, close |
| `wpf-navigation` | WPF Menu/MenuItem, ContextMenu host, TextBox, Button, Expander, Popup, ListBox/ListBoxItem, TabControl/TabItem, secondary Window | attach, repeated find/act across popup revisions, selection and expansion, element screenshot, waits, diagnose, close |
| `winforms-controls` | WinForms Form, TextBox, Label, Button, CheckBox, ComboBox, secondary Form | attach, WinForms property extraction, type/click/invoke/set/focus actions, whole-window and element capture, waits, diagnose, close |
| `screenshots` | WPF window, WPF TextBox, hosted WinForms Button | PNG/JPEG/BMP encodings, whole-window and element targets, image metadata/content/resource links |

The WPF and WinForms native file-dialog/message-box launch buttons are represented by the covered Button type but are not opened in the unattended suite: their modal native loops would block the target-side command that opened them. Dialog handling should be added as a dedicated cross-HWND feature rather than treated as an ordinary injected-tree control.
