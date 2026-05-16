namespace DeepFlowTest.AppDriverPayload.Commands;

using DeepFlowTest.Contracts;

internal sealed class HelloCommandHandler : ICommandHandler<HelloCommandRequest, object>, IImmediateCommandHandler
{
	public object Handle(HelloCommandRequest request, CommandContext context) =>
		HelloCommand.Process(request, context.Options, context.ReusableSession);
}

internal sealed class PipeStatusCommandHandler : ICommandHandler<PipeStatusCommandRequest, object>, IImmediateCommandHandler
{
	public object Handle(PipeStatusCommandRequest request, CommandContext context) =>
		PipeStatusCommand.Process(request, context.Options, context.ReusableSession);
}

internal sealed class StartSendingCommandHandler : ICommandHandler<StartSendingCommandRequest, object>, IImmediateCommandHandler
{
	public object Handle(StartSendingCommandRequest request, CommandContext context) =>
		StartSendingCommand.Process(request, context.Command, context.ReusableSession, context.TreeService);
}

internal sealed class StopSendingCommandHandler : ICommandHandler<StopSendingCommandRequest, object>, IImmediateCommandHandler
{
	public object Handle(StopSendingCommandRequest request, CommandContext context) =>
		StopSendingCommand.Process(request, context.ReusableSession);
}

internal sealed class PingCommandHandler : ICommandHandler<PingCommandRequest, object>, IUiCommandHandler
{
	public object Handle(PingCommandRequest request, CommandContext context) =>
		PingCommand.Process(request, context.TreeService);
}

internal sealed class GetVisualTreeCommandHandler : ICommandHandler<GetVisualTreeCommandRequest, object>, IUiCommandHandler
{
	public object Handle(GetVisualTreeCommandRequest request, CommandContext context) =>
		GetVisualTreeCommand.Process(request, context.TreeService);
}

internal sealed class FindElementCommandHandler : ICommandHandler<FindElementCommandRequest, object>, IUiCommandHandler
{
	public object Handle(FindElementCommandRequest request, CommandContext context) =>
		FindElementCommand.Process(request, context.TreeService, context.ExpressionCache);
}

internal sealed class ScreenshotCommandHandler : ICommandHandler<ScreenshotCommandRequest, object>, IUiCommandHandler
{
	public object Handle(ScreenshotCommandRequest request, CommandContext context) =>
		ScreenshotCommand.Process(request, context.TreeService);
}

internal sealed class ClickCommandHandler : ICommandHandler<ClickCommandRequest, object>, IUiCommandHandler
{
	public object Handle(ClickCommandRequest request, CommandContext context) =>
		TargetActionCommand.Click(request, context.TreeService);
}

internal sealed class FocusCommandHandler : ICommandHandler<FocusCommandRequest, object>, IUiCommandHandler
{
	public object Handle(FocusCommandRequest request, CommandContext context) =>
		TargetActionCommand.Focus(request, context.TreeService);
}

internal sealed class TypeTextCommandHandler : ICommandHandler<TypeTextCommandRequest, object>, IUiCommandHandler
{
	public object Handle(TypeTextCommandRequest request, CommandContext context) =>
		TargetActionCommand.TypeText(request, context.TreeService);
}

internal sealed class KeyPressCommandHandler : ICommandHandler<KeyPressCommandRequest, object>, IUiCommandHandler
{
	public object Handle(KeyPressCommandRequest request, CommandContext context) =>
		TargetActionCommand.KeyPress(request, context.TreeService);
}

internal sealed class SetPropertyCommandHandler : ICommandHandler<SetPropertyCommandRequest, object>, IUiCommandHandler
{
	public object Handle(SetPropertyCommandRequest request, CommandContext context) =>
		TargetActionCommand.SetProperty(request, context.TreeService);
}

internal sealed class RaiseEventCommandHandler : ICommandHandler<RaiseEventCommandRequest, object>, IUiCommandHandler
{
	public object Handle(RaiseEventCommandRequest request, CommandContext context) =>
		TargetActionCommand.RaiseEvent(request, context.TreeService);
}

internal sealed class KnownRoutedEventCommandHandler : ICommandHandler<KnownRoutedEventCommandRequest, object>, IUiCommandHandler
{
	public object Handle(KnownRoutedEventCommandRequest request, CommandContext context) =>
		TargetActionCommand.KnownRoutedEvent(request, context.TreeService);
}

internal sealed class KnownOperationCommandHandler : ICommandHandler<KnownOperationCommandRequest, object>, IUiCommandHandler
{
	public object Handle(KnownOperationCommandRequest request, CommandContext context) =>
		TargetActionCommand.KnownOperation(request, context.TreeService);
}

internal sealed class InvokeCommandHandler : ICommandHandler<InvokeCommandRequest, object>, IUiCommandHandler
{
	public object Handle(InvokeCommandRequest request, CommandContext context) =>
		TargetActionCommand.Invoke(request, context.TreeService);
}
