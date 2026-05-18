namespace DeepFlowTest.Mcp.Prompts;

using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerPromptType]
internal static class DeepFlowPrompts
{
	[McpServerPrompt(Name = "inspect_ui"), Description("Inspect the attached UI and identify stable selectors.")]
	public static string InspectUi() =>
		"""
		Start with deepflow_target_status. If no target is attached, use deepflow_list_processes, deepflow_attach_target, or deepflow_launch_target.
		Read the UI with deepflow_get_visual_tree using a small limit first. Use deepflow_find_elements and deepflow_suggest_selectors to identify stable selectors.
		Prefer automationId, name, text, typeName, and explicit properties over short IDs when reporting reusable selectors.
		""";

	[McpServerPrompt(Name = "drive_ui"), Description("Drive the target UI with policy-aware actions.")]
	public static string DriveUi() =>
		"""
		Check deepflow_target_status before acting. Use deepflow_find_elements to resolve the target, then call deepflow_click_element, deepflow_focus_element, deepflow_type_text, deepflow_press_keys, or deepflow_set_property.
		After each action, request after=target or after=tree when verification matters. If actions are denied, explain that the server must be started with the allowActions policy.
		Keep selectors stable enough to reuse in automated tests.
		""";

	[McpServerPrompt(Name = "diagnose_ui_failure"), Description("Diagnose missing elements, stale targets, binding errors, and unresponsive UI.")]
	public static string DiagnoseUiFailure() =>
		"""
		Check deepflow_target_status and deepflow_ping_target first. If the target is stale or exited, detach and attach or launch again.
		Use deepflow_get_visual_tree with refresh=true, then compare expected selectors with deepflow_find_elements. Read deepflow_get_binding_failures for WPF binding diagnostics.
		For timing issues, use deepflow_wait_for_element with a bounded timeout and include the final target status in your findings.
		""";

	[McpServerPrompt(Name = "author_test"), Description("Turn UI exploration into a maintainable DeepFlowTest test flow.")]
	public static string AuthorTest() =>
		"""
		Use deepflow_get_visual_tree, deepflow_find_elements, and deepflow_suggest_selectors to discover stable locators before proposing test code.
		Prefer assertions that verify visible UI state after each meaningful action. Use deepflow_capture_screenshot only when visual evidence is useful.
		Document required MCP policies such as allowLaunch, allowActions, or allowFileWrites near examples that depend on them.
		""";
}
