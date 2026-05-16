namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using DeepFlowTest.Interop;

internal sealed class ElementRegistry
{
	private readonly object sync = new();
	private readonly Dictionary<string, List<WeakReference<Element>>> elementsByTargetId = new(StringComparer.Ordinal);

	public void Register(Element element)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		lock (sync)
		{
			AddRegistration(element.TargetId, element);
		}
	}

	public void Move(Element element, string oldTargetId, string newTargetId)
	{
		_ = element ?? throw new ArgumentNullException(nameof(element));
		lock (sync)
		{
			RemoveRegistration(oldTargetId, element);
			AddRegistration(newTargetId, element);
		}
	}

	public void Refresh(VisualTreeSnapshot snapshot)
	{
		_ = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		lock (sync)
		{
			foreach (var node in snapshot.Nodes)
			{
				if (!elementsByTargetId.TryGetValue(node.TargetId, out var references))
					continue;

				for (var index = references.Count - 1; index >= 0; index--)
				{
					if (references[index].TryGetTarget(out var element))
						element.RefreshFromCache(node, snapshot);
					else
						references.RemoveAt(index);
				}

				if (references.Count == 0)
					elementsByTargetId.Remove(node.TargetId);
			}
		}
	}

	private void AddRegistration(string targetId, Element element)
	{
		if (!elementsByTargetId.TryGetValue(targetId, out var references))
		{
			references = [];
			elementsByTargetId[targetId] = references;
		}

		for (var index = references.Count - 1; index >= 0; index--)
		{
			if (!references[index].TryGetTarget(out var live))
			{
				references.RemoveAt(index);
				continue;
			}

			if (ReferenceEquals(live, element))
				return;
		}

		references.Add(new WeakReference<Element>(element));
	}

	private void RemoveRegistration(string targetId, Element element)
	{
		if (!elementsByTargetId.TryGetValue(targetId, out var references))
			return;

		for (var index = references.Count - 1; index >= 0; index--)
		{
			if (!references[index].TryGetTarget(out var live) || ReferenceEquals(live, element))
				references.RemoveAt(index);
		}

		if (references.Count == 0)
			elementsByTargetId.Remove(targetId);
	}
}
