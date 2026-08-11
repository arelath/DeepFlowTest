namespace DeepFlowTest.Mcp.Prompts;

using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerPromptType]
internal static class DeepFlowPrompts
{
	[McpServerPrompt(Name = "inspect_ui"), Description("Inspect the attached UI and identify stable selectors.")]
	public static string InspectUi() =>
		"""
		Start with deepflow_open_context and retain its contextId. Read the UI with deepflow_observe using the condensed format first.
		Use deepflow_find with semantic selectors to identify stable element handles. Prefer automationId, name, text, type, and typed property matches over runtime target IDs.
		Use the immutable snapshot resource link from deepflow_observe when the compact view is insufficient.
		""";

	[McpServerPrompt(Name = "drive_ui"), Description("Drive the target UI with policy-aware actions.")]
	public static string DriveUi() =>
		"""
		Open a context, observe, and use deepflow_find to obtain a stable handle. Use deepflow_act with a discriminated click, type, key, set, focus, invoke, or drag action.
		Declare an expectation when the resulting property is known; deepflow_act resolves, acts, verifies, and returns a condensed delta by default. If actions are denied, explain that the server must be started with allowActions.
		On ambiguity, choose one of the returned candidate handles or refine the semantic selector. Do not request first-match behavior unless order is intentional.
		""";

	[McpServerPrompt(Name = "diagnose_ui_failure"), Description("Diagnose missing elements, stale targets, binding errors, and unresponsive UI.")]
	public static string DiagnoseUiFailure() =>
		"""
		Use deepflow_diagnose for target health and binding failures. If the context is stale or the target exited, close it if possible and open a new context.
		Use deepflow_observe with refresh=true, then compare expected semantic selectors with deepflow_find. Stable handles are repaired automatically during deepflow_act.
		For timing issues, use deepflow_wait with a bounded timeout; it supports disappearance, counts, property state, UI stability, responsiveness, and window-title changes.
		""";

	[McpServerPrompt(Name = "author_test"), Description("Turn UI exploration into a maintainable DeepFlowTest test flow.")]
	public static string AuthorTest() =>
		"""
		Use deepflow_observe and deepflow_find to discover transport-neutral semantic selectors before proposing test code.
		Prefer expectations that verify visible UI state after each meaningful deepflow_act call. Use deepflow_capture only when visual evidence is useful.
		Document required MCP policies such as allowLaunch or allowActions near examples that depend on them.
		""";
}
