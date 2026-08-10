namespace DeepFlowTest.Contracts;

using System.Collections.Generic;

public static class KnownProperties
{
	public const string AutomationId = "AutomationProperties.AutomationId";
	public const string AutomationIdAlias = "AutomationId";
	public const string AutomationName = "AutomationProperties.Name";
	public const string AutomationNameAlias = "AutomationName";
	public const string Bindings = "Bindings";
	public const string Checked = "Checked";
	public const string ClassName = "ClassName";
	public const string Content = "Content";
	public const string ControlId = "ControlId";
	public const string FileName = "FileName";
	public const string Header = "Header";
	public const string Hwnd = "Hwnd";
	public const string Id = "Id";
	public const string ImageMetadata = "ImageMetadata";
	public const string IsChecked = "IsChecked";
	public const string IsEnabled = "IsEnabled";
	public const string IsExpanded = "IsExpanded";
	public const string IsFocused = "IsFocused";
	public const string IsKeyboardFocused = "IsKeyboardFocused";
	public const string IsKeyboardFocusWithin = "IsKeyboardFocusWithin";
	public const string IsOpen = "IsOpen";
	public const string IsSelected = "IsSelected";
	public const string IsSubmenuOpen = "IsSubmenuOpen";
	public const string IsVisible = "IsVisible";
	public const string MergedDictionaryCount = "MergedDictionaryCount";
	public const string Name = "Name";
	public const string ResourceKeys = "ResourceKeys";
	public const string ResourceOrigin = "ResourceOrigin";
	public const string Source = "Source";
	public const string Text = "Text";
	public const string Title = "Title";
	public const string Uid = "Uid";
	public const string Value = "Value";
	public const string Visibility = "Visibility";
	public const string Xaml = "Xaml";

	private static readonly IReadOnlyList<string> DefaultVisualTreePropertyNamesValue =
	[
		Name,
		AutomationName,
		AutomationId,
		Text,
		Content,
		IsVisible,
		IsEnabled,
	];

	private static readonly IReadOnlyList<string> TextualIdentityPropertyNamesValue =
	[
		Text,
		Content,
		Header,
		Title,
	];

	public static IReadOnlyList<string> DefaultVisualTreePropertyNames => DefaultVisualTreePropertyNamesValue;

	public static IReadOnlyList<string> TextualIdentityPropertyNames => TextualIdentityPropertyNamesValue;
}
