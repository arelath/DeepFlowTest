namespace DeepFlowTest;

public interface IAppDriverBackend
{
	AppConnection Launch(string executablePath, AppDriverLaunchOptions options);

	AppConnection AttachTo(int processId, AppDriverAttachOptions options);

	AppConnection AttachTo(string processName, AppDriverAttachOptions options);
}
