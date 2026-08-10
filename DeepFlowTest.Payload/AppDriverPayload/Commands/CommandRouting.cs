namespace DeepFlowTest.AppDriverPayload.Commands;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DeepFlowTest.Contracts;
using DeepFlowTest.Interop;
using DeepFlowTest.Utility.WpfUtility.Tree;

internal interface ICommandHandler<in TRequest, out TResponse>
	where TRequest : IpcCommand
{
	TResponse Handle(TRequest request, CommandContext context);
}

internal interface IImmediateCommandHandler
{
}

internal interface IUiCommandHandler
{
}

internal sealed class CommandContext(
	NamedPipeServer.Command command,
	AppDriverPayloadStartupOptions options,
	ReusablePipeSession? reusableSession,
	TreeService treeService,
	ExpressionCache expressionCache)
{
	public NamedPipeServer.Command Command { get; } = command;

	public AppDriverPayloadStartupOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

	public ReusablePipeSession? ReusableSession { get; } = reusableSession;

	public TreeService TreeService { get; } = treeService ?? throw new ArgumentNullException(nameof(treeService));

	public ExpressionCache ExpressionCache { get; } = expressionCache ?? throw new ArgumentNullException(nameof(expressionCache));

	public string LogCorrelationId { get; } = PayloadLog.CurrentCorrelationId;
}

internal sealed class CommandHandlerRegistry
{
	private CommandHandlerRegistry(
		IReadOnlyDictionary<string, RegisteredCommandHandler> immediateHandlers,
		IReadOnlyDictionary<string, RegisteredCommandHandler> uiHandlers)
	{
		ImmediateHandlers = immediateHandlers;
		UiHandlers = uiHandlers;
		AllHandlers = immediateHandlers.Values
			.Concat(uiHandlers.Values)
			.OrderBy(static handler => handler.Kind, StringComparer.Ordinal)
			.ToArray();
	}

	public IReadOnlyDictionary<string, RegisteredCommandHandler> ImmediateHandlers { get; }

	public IReadOnlyDictionary<string, RegisteredCommandHandler> UiHandlers { get; }

	public IReadOnlyList<RegisteredCommandHandler> AllHandlers { get; }

	public static CommandHandlerRegistry CreateDefault() =>
		Create(typeof(CommandHandlerRegistry).Assembly, typeof(IpcCommand).Assembly);

	internal static CommandHandlerRegistry Create(Assembly handlerAssembly, Assembly commandAssembly)
	{
		_ = handlerAssembly ?? throw new ArgumentNullException(nameof(handlerAssembly));
		_ = commandAssembly ?? throw new ArgumentNullException(nameof(commandAssembly));

		var immediateHandlers = new Dictionary<string, RegisteredCommandHandler>(StringComparer.Ordinal);
		var uiHandlers = new Dictionary<string, RegisteredCommandHandler>(StringComparer.Ordinal);

		foreach (var handlerType in FindHandlerTypes(handlerAssembly))
		{
			var handlerInterface = GetSingleHandlerInterface(handlerType);
			var genericArguments = handlerInterface.GetGenericArguments();
			var requestType = genericArguments[0];
			var responseType = genericArguments[1];
			var kind = GetKindForRequestType(requestType);
			var handler = Activator.CreateInstance(handlerType, nonPublic: true)
				?? throw new InvalidOperationException($"Command handler '{handlerType.FullName}' could not be created.");
			var registered = RegisteredCommandHandler.Create(kind, requestType, responseType, handler);

			var isImmediate = typeof(IImmediateCommandHandler).IsAssignableFrom(handlerType);
			var isUi = typeof(IUiCommandHandler).IsAssignableFrom(handlerType);
			if (isImmediate == isUi)
				throw new InvalidOperationException($"Command handler '{handlerType.FullName}' must implement exactly one dispatch marker.");

			AddHandler(isImmediate ? immediateHandlers : uiHandlers, registered);
		}

		ValidateAllCommandsHaveHandlers(commandAssembly, immediateHandlers, uiHandlers);
		return new CommandHandlerRegistry(immediateHandlers, uiHandlers);
	}

	private static IEnumerable<Type> FindHandlerTypes(Assembly assembly) =>
		assembly.GetTypes()
			.Where(static type => !type.IsAbstract && !type.IsInterface)
			.Where(static type => type.GetInterfaces().Any(IsCommandHandlerInterface));

	private static Type GetSingleHandlerInterface(Type handlerType)
	{
		var handlerInterfaces = handlerType.GetInterfaces()
			.Where(IsCommandHandlerInterface)
			.ToArray();

		if (handlerInterfaces.Length != 1)
			throw new InvalidOperationException($"Command handler '{handlerType.FullName}' must implement exactly one ICommandHandler<TRequest, TResponse> interface.");

		var requestType = handlerInterfaces[0].GetGenericArguments()[0];
		if (!typeof(IpcCommand).IsAssignableFrom(requestType) || requestType.IsAbstract)
			throw new InvalidOperationException($"Command handler '{handlerType.FullName}' uses an invalid command request type '{requestType.FullName}'.");

		return handlerInterfaces[0];
	}

	private static bool IsCommandHandlerInterface(Type type) =>
		type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>);

	private static string GetKindForRequestType(Type requestType)
	{
		if (Activator.CreateInstance(requestType, nonPublic: true) is not IpcCommand command)
			throw new InvalidOperationException($"Command request '{requestType.FullName}' must have a parameterless constructor.");

		if (string.IsNullOrWhiteSpace(command.Kind))
			throw new InvalidOperationException($"Command request '{requestType.FullName}' has an empty kind.");

		return command.Kind;
	}

	private static void AddHandler(Dictionary<string, RegisteredCommandHandler> handlers, RegisteredCommandHandler handler)
	{
		if (handlers.ContainsKey(handler.Kind))
			throw new InvalidOperationException($"Command kind '{handler.Kind}' has more than one registered handler.");

		handlers.Add(handler.Kind, handler);
	}

	private static void ValidateAllCommandsHaveHandlers(
		Assembly commandAssembly,
		IReadOnlyDictionary<string, RegisteredCommandHandler> immediateHandlers,
		IReadOnlyDictionary<string, RegisteredCommandHandler> uiHandlers)
	{
		foreach (var requestType in FindCommandRequestTypes(commandAssembly))
		{
			var kind = GetKindForRequestType(requestType);
			var immediateMatch = immediateHandlers.ContainsKey(kind);
			var uiMatch = uiHandlers.ContainsKey(kind);
			if (immediateMatch == uiMatch)
				throw new InvalidOperationException($"Command request '{requestType.FullName}' must have exactly one handler for kind '{kind}'.");
		}
	}

	private static IEnumerable<Type> FindCommandRequestTypes(Assembly assembly) =>
		assembly.GetTypes()
			.Where(static type => !type.IsAbstract && typeof(IpcCommand).IsAssignableFrom(type));
}

internal sealed class RegisteredCommandHandler
{
	private RegisteredCommandHandler(
		string kind,
		Type requestType,
		Type responseType,
		object handler,
		Func<object, object, CommandContext, object> invoke)
	{
		Kind = kind;
		RequestType = requestType;
		ResponseType = responseType;
		this.handler = handler;
		this.invoke = invoke;
	}

	private readonly object handler;
	private readonly Func<object, object, CommandContext, object> invoke;

	public string Kind { get; }

	public Type RequestType { get; }

	public Type ResponseType { get; }

	public static RegisteredCommandHandler Create(string kind, Type requestType, Type responseType, object handler)
	{
		var invoke = CreateInvoker(requestType, responseType);
		return new RegisteredCommandHandler(kind, requestType, responseType, handler, invoke);
	}

	public object Handle(CommandContext context)
	{
		var request = MessagePacker.ConvertTo(context.Command.Value, RequestType);
		return invoke(handler, request, context);
	}

	private static Func<object, object, CommandContext, object> CreateInvoker(Type requestType, Type responseType)
	{
		var method = typeof(RegisteredCommandHandler)
			.GetMethod(nameof(CreateInvokerCore), BindingFlags.Static | BindingFlags.NonPublic)!
			.MakeGenericMethod(requestType, responseType);
		return (Func<object, object, CommandContext, object>)method.Invoke(null, null)!;
	}

	private static Func<object, object, CommandContext, object> CreateInvokerCore<TRequest, TResponse>()
		where TRequest : IpcCommand
	{
		return (handler, request, context) =>
		{
			var response = ((ICommandHandler<TRequest, TResponse>)handler).Handle((TRequest)request, context);
			return response is null ? StandardIpcResponse.Ok() : response;
		};
	}
}
