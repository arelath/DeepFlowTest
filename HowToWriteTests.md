# How to Use DeepFlowTest

DeepFlowTest is a UI automation framework for WPF and WinForms applications. It works by injecting a lightweight payload directly into your application, allowing you to query and manipulate the visual tree instantly using standard C# expressions.

## Getting Started

The main entry point for automation is the `AppDriver`. Wrap it in a `using` statement so that the application is properly closed when the test finishes.

```csharp
using NUnit.Framework;
using DeepFlowTest;

[TestFixture]
public class MyFirstTest
{
    [Test]
    public void AppShouldLaunchAndClickButton()
    {
        // Launch the application
        using var driver = AppDriver.Launch(@"C:\Path\To\YourApp.exe");

        // Find a button and click it
        var button = driver.GetElement(ElementSelector.ByAutomationId("SubmitButton"));
        button.Click();
    }
}
```

*Note: You can also attach to an already running application using `AppDriver.AttachTo("YourApp");` or `AppDriver.AttachTo(processId);`.*

## Finding Elements

You can find elements using the `ElementSelector` helpers, or by writing LINQ expressions. The driver will automatically wait (poll) for the element to appear within a default timeout.

### 1. Using Selectors (Simple)
```csharp
var el1 = driver.GetElement(ElementSelector.ByAutomationId("LoginBtn"));
var el2 = driver.GetElement(ElementSelector.ByName("FirstNameTextBox"));
var el3 = driver.GetElement(ElementSelector.ByText("Submit"));
var el4 = driver.GetElement(ElementSelector.ByType("Button"));
```

### 2. Using Expressions (Advanced & Powerful)
DeepFlowTest allows you to write queries that evaluate *inside* the target application. You can query any UI property using the indexer `["PropertyName"]`.

```csharp
// Find a button that contains the text "Save" and is enabled
var saveBtn = driver.GetElement(x => 
    x.TypeName == "Button" && 
    x["Content"] == "Save" && 
    x["IsEnabled"] == true);
```

### 3. Finding Multiple Elements
If you expect multiple elements to match, use `GetElements`.
```csharp
// Returns a list of all checked CheckBoxes
var checkedBoxes = driver.GetElements(x => 
    x.TypeName == "CheckBox" && 
    x["IsChecked"] == true);
```

### 4. Scoped Searches
To search *inside* a specific element to avoid ambiguity:
```csharp
var panel = driver.GetElement(ElementSelector.ByName("SettingsPanel"));

// Only searches inside the SettingsPanel
var saveBtn = driver.GetElement(panel, x => x.TypeName == "Button" && x["Content"] == "Save");
```

## Interacting with Elements

Once you have an `Element`, you can chain actions to interact with it.

```csharp
var usernameBox = driver.GetElement(ElementSelector.ByAutomationId("UsernameInput"));

// Type text (clearFirst: true will empty the textbox before typing)
usernameBox.Type("admin", clearFirst: true);

var loginBtn = driver.GetElement(ElementSelector.ByAutomationId("LoginButton"));

// Mouse actions
loginBtn.Click();
loginBtn.DoubleClick();
loginBtn.RightClick();
loginBtn.MiddleClick();

// Focus
loginBtn.Focus();
```

### Specialized UI Actions
DeepFlowTest knows how to interact with common WPF/WinForms controls without simulating raw mouse clicks:

```csharp
var checkbox = driver.GetElement(ElementSelector.ByName("RememberMe"));
checkbox.Check();
checkbox.Uncheck();

var combo = driver.GetElement(ElementSelector.ByName("CountryDropdown"));
combo.Expand();
combo.Collapse();

var listItem = driver.GetElement(ElementSelector.ByText("Canada"));
listItem.Select();
```

## Reading and Setting Properties

You can read properties directly from the UI thread and set them.

```csharp
var textBlock = driver.GetElement(ElementSelector.ByAutomationId("StatusText"));

// Reading properties (automatically converts to the requested type)
string status = textBlock.GetProperty<string>("Text");
bool isEnabled = textBlock.GetProperty<bool>("IsEnabled");

// Setting properties directly
textBlock.SetProperty("Text", "Loading complete...");
```

## Assertions

DeepFlowTest includes built-in assertion helpers that wait for the condition to be true before failing. It integrates cleanly with standard test frameworks (NUnit, xUnit, MSTest).

```csharp
var statusLabel = driver.GetElement(ElementSelector.ByAutomationId("StatusLabel"));

// Verify standard properties
statusLabel.ShouldBeVisible();
statusLabel.ShouldHaveProperty("Text", "Success");

// Use an expression to assert complex logic (waits up to 5 seconds)
statusLabel.Assert(x => x["Text"] == "Success" && x["Foreground"].ToString().Contains("Green"), timeoutMs: 5000);
```

## Keyboard Input

Sometimes you need to simulate keyboard input. Use the `driver.Keyboard` API
for physical foreground input, or pass an `Element` when the input should be
sent through the target-side command pipeline.

```csharp
using System.Windows.Input;

// Type raw text into the active window
driver.Keyboard.Type("Hello World!");

// Press physical keys
driver.Keyboard.Press(Key.Enter);
driver.Keyboard.Press(Key.Tab);

// Target an element through the injected payload
var input = driver.GetElement(ElementSelector.ByAutomationId("UsernameInput"));
driver.Keyboard.Type(input, "admin", clearFirst: true);
driver.Keyboard.Press(input, "Tab");
driver.Keyboard.Shortcut(input, "Control", "A"); // Select All
```

## Handling Dialogs

Native Windows dialogs (like `MessageBox` or `OpenFileDialog`) block the UI thread. DeepFlowTest provides specific extensions to handle these cleanly.

```csharp
// Click the button that opens the dialog
driver.GetElement(ElementSelector.ByAutomationId("UploadButton")).Click();

// Easily interact with the Open File dialog
driver.HandleFileDialog(@"C:\TestData\image.png");

// Or simply accept/dismiss a MessageBox
driver.AcceptDialog();
driver.CancelDialog();
```

## Screenshots and Video Recording

Visual debugging is built right in.

### Screenshots
```csharp
// Screenshot the entire application
driver.Screenshot(@"C:\TestResults\App.png");

// Screenshot a specific element
var grid = driver.GetElement(ElementSelector.ByName("DataGrid"));
grid.Screenshot(@"C:\TestResults\Grid.jpg");
```

### Video Recording (Requires FFmpeg)
You can record the application during the test. The recording stops automatically when the `using` block ends.

```csharp
[Test]
public void TestWithVideo()
{
    using var driver = AppDriver.Launch("MyApp.exe");
    
    // Start recording
    using var video = AppDriver.Record(@"C:\TestResults\TestRun.mp4");
    
    driver.GetElement(ElementSelector.ByName("Login")).Click();
    // ... test continues ...
}
```

***

## A Complete Example

Here is what a full test looks like from start to finish:

```csharp
using NUnit.Framework;
using DeepFlowTest;

[TestFixture]
public class LoginTests
{
    [Test]
    public void ValidUser_ShouldSuccessfullyLogin()
    {
        using var driver = AppDriver.Launch("MyApplication.exe");

        // 1. Enter credentials
        driver.GetElement(ElementSelector.ByAutomationId("txtUsername"))
              .Type("TestUser", clearFirst: true);

        driver.GetElement(ElementSelector.ByAutomationId("txtPassword"))
              .Type("SuperSecret!", clearFirst: true);

        // 2. Click Login
        driver.GetElement(ElementSelector.ByAutomationId("btnLogin")).Click();

        // 3. Assert success message appears
        driver.GetElement(ElementSelector.ByAutomationId("lblStatus"))
              .Assert(x => x["Text"] == "Welcome, TestUser!");
              
        // 4. Accept the welcome popup
        driver.AcceptDialog();
    }
}
```
