namespace DeepFlowTest.Contracts;

using System.Collections.Generic;
using System.Reflection;

public static class IpcCommandExtensions
{
	public static Dictionary<string, object> ToDictionary(this IpcCommand command)
	{
		_ = command ?? throw new System.ArgumentNullException(nameof(command));
		var result = new Dictionary<string, object>();
		foreach (var property in command.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
		{
			var value = property.GetValue(command);
			if (value is not null)
				result[property.Name] = value;
		}

		return result;
	}
}
