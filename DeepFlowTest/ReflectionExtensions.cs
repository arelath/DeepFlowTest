namespace DeepFlowTest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

public static class ReflectionExtensions
{
	public static T Invoke<T>(this Type type, string methodName, params object?[]? args)
	{
		var result = InvokeCore(type, null, methodName, args);
		return (T)result!;
	}

	public static void Invoke(this Type type, string methodName, params object?[]? args)
	{
		InvokeCore(type, null, methodName, args);
	}

	public static T InvokeOn<T>(this Type type, object? target, string methodName, params object?[]? args)
	{
		var result = InvokeCore(type, target, methodName, args);
		return (T)result!;
	}

	public static void InvokeOn(this Type type, object? target, string methodName, params object?[]? args)
	{
		InvokeCore(type, target, methodName, args);
	}

	public static T Invoke<T>(this object obj, string methodName, params object?[]? args)
	{
		_ = obj ?? throw new ArgumentNullException(nameof(obj));
		var result = InvokeCore(obj.GetType(), obj, methodName, args);
		return (T)result!;
	}

	public static void Invoke(this object obj, string methodName, params object?[]? args)
	{
		_ = obj ?? throw new ArgumentNullException(nameof(obj));
		InvokeCore(obj.GetType(), obj, methodName, args);
	}

	public static T Field<T>(this object obj, string fieldName)
	{
		_ = obj ?? throw new ArgumentNullException(nameof(obj));
		var fieldInfo = obj.GetType().GetFields(InvokeInstanceBindings)
			.Where(field => field.Name == fieldName && field.FieldType == typeof(T))
			.OrderBy(GetFieldAccessRank)
			.FirstOrDefault()
			?? throw new ArgumentException($"No field found with name `{fieldName}` on type `{obj.GetType().Name}`.");

		return (T)fieldInfo.GetValue(obj)!;
	}

	public static void SetField<T>(this object obj, string fieldName, T value)
	{
		_ = obj ?? throw new ArgumentNullException(nameof(obj));
		var fieldInfo = obj.GetType().GetFields(InvokeInstanceBindings)
			.Where(field => field.Name == fieldName && field.FieldType == typeof(T))
			.OrderBy(GetFieldAccessRank)
			.FirstOrDefault()
			?? throw new ArgumentException($"No field found with name `{fieldName}` on type `{obj.GetType().Name}`.");

		fieldInfo.SetValue(obj, value);
	}

	public static T Property<T>(this object obj, string propertyName)
	{
		_ = obj ?? throw new ArgumentNullException(nameof(obj));
		var propertyInfo = obj.GetType().GetProperties(InvokeInstanceBindings)
			.FirstOrDefault(property => property.Name == propertyName && property.PropertyType == typeof(T))
			?? throw new ArgumentException($"No property found with name `{propertyName}` on type `{obj.GetType().Name}`.");

		return (T)propertyInfo.GetValue(obj)!;
	}

	public static void SetProperty<T>(this object obj, string propertyName, T value)
	{
		_ = obj ?? throw new ArgumentNullException(nameof(obj));
		var propertyInfo = obj.GetType().GetProperties(InvokeInstanceBindings)
			.FirstOrDefault(property => property.Name == propertyName && property.PropertyType == typeof(T))
			?? throw new ArgumentException($"No property found with name `{propertyName}` on type `{obj.GetType().Name}`.");

		propertyInfo.SetValue(obj, value);
	}

	private static object? InvokeCore(Type type, object? target, string methodName, object?[]? args)
	{
		_ = type ?? throw new ArgumentNullException(nameof(type));
		if (string.IsNullOrWhiteSpace(methodName))
			throw new ArgumentException("Method name is required.", nameof(methodName));

		var methodArgs = args ?? [];
		var methods = GetCandidateMethods(type, methodName, InvokeAllBindings, methodArgs);
		if (methods.Count == 0)
			throw new ArgumentException($"No method with name `{methodName}` on type `{type.Name}` has a signature matching the given args.");

		return FindAndInvokeBestMatch(methods, target, methodArgs);
	}

	private static object? FindAndInvokeBestMatch(IReadOnlyList<MethodInfo> methods, object? target, object?[] args)
	{
		var method = methods.FirstOrDefault(method => ParametersMatch(method.GetParameters(), args))
			?? throw new ArgumentException("Failed to match any methods.");

		try
		{
			return method.Invoke(target, args);
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			throw;
		}
	}

	private static IReadOnlyList<MethodInfo> GetCandidateMethods(Type type, string methodName, BindingFlags bindingFlags, object?[] args)
	{
		return type.GetMethods(bindingFlags)
			.Where(method => method.Name == methodName)
			.Where(method => method.GetParameters().Length == args.Length)
			.Where(method => ParametersMatch(method.GetParameters(), args))
			.OrderBy(GetMethodAccessRank)
			.ThenBy(method => GetParameterMatchScore(method.GetParameters(), args))
			.ToArray();
	}

	private static bool ParametersMatch(ParameterInfo[] parameterInfos, object?[] args)
	{
		if (parameterInfos.Length != args.Length)
			return false;

		for (var i = 0; i < parameterInfos.Length; i++)
		{
			var parameterType = GetEffectiveParameterType(parameterInfos[i].ParameterType);
			var arg = args[i];
			if (arg is null)
			{
				if (!CanAcceptNull(parameterType))
					return false;

				continue;
			}

			if (!ArgumentTypeMatches(parameterType, arg.GetType()))
				return false;
		}

		return true;
	}

	private static bool ArgumentTypeMatches(Type parameterType, Type argType)
	{
		var nullableType = Nullable.GetUnderlyingType(parameterType);
		return nullableType is not null
			? nullableType.IsAssignableFrom(argType)
			: parameterType.IsAssignableFrom(argType);
	}

	private static bool CanAcceptNull(Type parameterType) =>
		!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) is not null;

	private static Type GetEffectiveParameterType(Type parameterType) =>
		parameterType.IsByRef ? parameterType.GetElementType() ?? parameterType : parameterType;

	private static int GetParameterMatchScore(ParameterInfo[] parameterInfos, object?[] args)
	{
		var score = 0;
		for (var i = 0; i < parameterInfos.Length; i++)
		{
			var arg = args[i];
			if (arg is null)
			{
				score++;
				continue;
			}

			var parameterType = GetEffectiveParameterType(parameterInfos[i].ParameterType);
			var argType = arg.GetType();
			var nullableType = Nullable.GetUnderlyingType(parameterType);
			if (parameterType != argType && nullableType != argType)
				score++;
		}

		return score;
	}

	private static int GetFieldAccessRank(FieldInfo field) =>
		field.IsPublic ? 0 : field.IsAssembly ? 1 : 2;

	private static int GetMethodAccessRank(MethodInfo method) =>
		method.IsPublic ? 0 : method.IsAssembly ? 1 : 2;

	private const BindingFlags InvokeAllBindings = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
	private const BindingFlags InvokeInstanceBindings = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
}
