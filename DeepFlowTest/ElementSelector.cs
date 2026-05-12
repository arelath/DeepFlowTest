namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using DeepFlowTest.Contracts;

public sealed class ElementSelector
{
	private readonly ElementSelectorDto dto = new();

	private ElementSelector()
	{
	}

	public IReadOnlyList<string>? RequestedPropertyNames { get; private set; }

	public static ElementSelector ByName(string name)
	{
		var selector = new ElementSelector();
		selector.dto.Name = name;
		return selector;
	}

	public static ElementSelector ByAutomationId(string automationId)
	{
		var selector = new ElementSelector();
		selector.dto.AutomationId = automationId;
		return selector;
	}

	public static ElementSelector ByText(string text)
	{
		var selector = new ElementSelector();
		selector.dto.Text = text;
		return selector;
	}

	public static ElementSelector ByContent(string content)
	{
		var selector = new ElementSelector();
		selector.dto.Content = content;
		return selector;
	}

	public static ElementSelector ByType(string typeName)
	{
		var selector = new ElementSelector();
		selector.dto.TypeName = typeName;
		return selector;
	}

	public ElementSelector WithProperty(string propertyName, object? value)
	{
		dto.Properties[propertyName] = value;
		return this;
	}

	public ElementSelector WithRequestedProperties(params string[] propertyNames)
	{
		RequestedPropertyNames = propertyNames ?? Array.Empty<string>();
		return this;
	}

	public ElementSelectorDto ToDto()
	{
		return new ElementSelectorDto
		{
			TypeName = dto.TypeName,
			Name = dto.Name,
			AutomationId = dto.AutomationId,
			Text = dto.Text,
			Content = dto.Content,
			Properties = new Dictionary<string, object?>(dto.Properties, StringComparer.Ordinal),
		};
	}

	public override string ToString()
	{
		if (!string.IsNullOrWhiteSpace(dto.Name))
			return $"Name={dto.Name}";
		if (!string.IsNullOrWhiteSpace(dto.AutomationId))
			return $"AutomationId={dto.AutomationId}";
		if (!string.IsNullOrWhiteSpace(dto.Text))
			return $"Text={dto.Text}";
		if (!string.IsNullOrWhiteSpace(dto.Content))
			return $"Content={dto.Content}";
		if (!string.IsNullOrWhiteSpace(dto.TypeName))
			return $"Type={dto.TypeName}";
		return "ElementSelector";
	}
}
