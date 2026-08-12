# CLI E2E coverage

| Scenario | Application | Coverage |
|---|---|---|
| `foundation` | none | help, version, process discovery, isolated config get/set/clear/reset, JSON config values, invalid-argument envelope |
| `wpf-inspection` | HelloWorld | PID and window-title targeting, listener reuse/no-inject, ping, pipe status, flat/nested and text tree output, deep find, node, props, selectors, wait, no-match, action and arbitrary-invoke policy failures |
| `wpf-actions` | HelloWorld | focus, type, key, set, routed-event raise, click, drag, known invoke operations, delayed wait, popup, list, expander, scroll content, hosted WinForms controls, durable state reads |
| `wpf-navigation` | BasicTestHarness | menu/menu item, text box, button, expander, popup, list box/item, tab control/item, secondary window |
| `winforms-controls` | WinFormsExampleApp | form, text box, label, button, check box, combo box, secondary form, durable state reads |
| `screenshots` | HelloWorld | full-window PNG/JPEG/BMP/GIF plus WPF-element and hosted-WinForms-element PNG capture; byte size and file signatures |
| `streams-and-recording` | HelloWorld | visual-tree, visual-tree-delta, screenshot, event-log, binding-failure, semantic-recording streams; condensed and compact-JSON recording files |

The suite exercises every public top-level command family and every control category present in the three unattended harnesses. Native OS modal dialogs remain outside the unattended lane because opening them synchronously occupies the reusable CLI pipe; their accept/cancel behavior remains covered below the process boundary by unit and integration tests.

Raw screenshot-stream frames are intentionally limited to a small element and short duration. Screenshot commands write bytes to files instead of embedding base64 in JSON. These bounds reduce log size and agent token usage without weakening protocol coverage.
