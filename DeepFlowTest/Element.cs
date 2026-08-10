namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;

public partial class Element
{
	private ElementSelector? selector;
	private ElementRepairInfo? repairInfo;
	private IReadOnlyList<ElementPathSegmentResponse> diagnosticPath = [];

	internal Element(
		AppDriver driver,
		VisualTreeNodeDto node,
		ElementSelector? selector = null,
		VisualTreeSnapshot? snapshot = null,
		ElementRepairInfo? repairInfo = null,
		IReadOnlyList<ElementPathSegmentResponse>? diagnosticPath = null,
		bool register = true)
		: this(new ClientElementContext(driver), node, snapshot)
	{
		this.selector = selector;
		this.repairInfo = repairInfo;
		this.diagnosticPath = diagnosticPath ?? [];
		if (register)
			driver.RegisterElement(this);
	}

	public ElementSelector? Selector => selector;

	internal ElementRepairInfo? RepairInfo => repairInfo;

	internal IReadOnlyList<ElementPathSegmentResponse> DiagnosticPath => diagnosticPath;

	private AppDriver Driver =>
		Context is ClientElementContext clientContext
			? clientContext.Driver
			: throw new InvalidOperationException("This element is only available while evaluating a target-side expression and cannot perform driver actions.");

	private ElementCommandExecutor Commands => Driver.ElementCommandExecutor;

	internal static Element FromMatch(
		AppDriver driver,
		FindElementMatchResponse match,
		ElementSelector? selector,
		ElementRepairInfo? repairInfo = null)
	{
		return new Element(
			driver,
			new VisualTreeNodeDto
			{
				TargetId = match.TargetId,
				TypeName = match.TypeName,
				FrameworkTypeName = match.FrameworkTypeName,
				Properties = match.Properties,
			},
			selector,
			repairInfo: repairInfo,
			diagnosticPath: match.Path);
	}

	internal static Element FromNode(
		AppDriver driver,
		VisualTreeNodeDto node,
		VisualTreeSnapshot snapshot,
		ElementRepairInfo? repairInfo = null,
		bool register = true) =>
		new(driver, node, snapshot: snapshot, repairInfo: repairInfo, register: register);

	partial void CopyRuntimeStateFrom(Element source)
	{
		selector = source.selector;
		repairInfo = source.repairInfo;
		diagnosticPath = source.diagnosticPath;
		if (Context is ClientElementContext clientContext)
			clientContext.Driver.RegisterElement(this);
	}
}
