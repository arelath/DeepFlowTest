namespace DeepFlowTest.Mcp.Resources;

using System;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

[McpServerResourceType]
internal sealed class AgentResources
{
	[McpServerResource(Name = "deepflow_context_snapshot", UriTemplate = "deepflow://contexts/{contextId}/snapshots/{artifactId}", MimeType = "application/json")]
	[Description("Read an immutable context-qualified visual-tree snapshot returned by deepflow_observe.")]
	public string ContextSnapshot(IServiceProvider services, string contextId, string artifactId) =>
		services.GetRequiredService<DeepFlowResourceStore>().ReadText($"deepflow://contexts/{contextId}/snapshots/{artifactId}");

	[McpServerResource(Name = "deepflow_context_screenshot", UriTemplate = "deepflow://contexts/{contextId}/screenshots/{artifactId}", MimeType = "application/json")]
	[Description("Read immutable screenshot metadata and base64 bytes returned by deepflow_capture.")]
	public string ContextScreenshot(IServiceProvider services, string contextId, string artifactId) =>
		services.GetRequiredService<DeepFlowResourceStore>().ReadText($"deepflow://contexts/{contextId}/screenshots/{artifactId}");

	[McpServerResource(Name = "deepflow_context_diagnostic", UriTemplate = "deepflow://contexts/{contextId}/diagnostics/{diagnosticKind}/{artifactId}", MimeType = "application/json")]
	[Description("Read an immutable context-qualified diagnostic artifact returned by deepflow_diagnose.")]
	public string ContextDiagnostic(IServiceProvider services, string contextId, string diagnosticKind, string artifactId) =>
		services.GetRequiredService<DeepFlowResourceStore>().ReadText($"deepflow://contexts/{contextId}/diagnostics/{diagnosticKind}/{artifactId}");
}
