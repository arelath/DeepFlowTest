namespace DeepFlowTest.Recorder;

using DeepFlowTest.Contracts;

public sealed class RecordingFrameViewModel : ObservableObject
{
	public RecordingFrameViewModel(
		int frameNumber,
		int sourceFrameIndex,
		SemanticRecordingFrame sourceFrame,
		string summary,
		string? errorDetails)
	{
		FrameNumber = frameNumber;
		SourceFrameIndex = sourceFrameIndex;
		SourceFrame = sourceFrame;
		SequenceNumber = sourceFrame.SequenceNumber;
		Kind = sourceFrame.FrameKind ?? string.Empty;
		Summary = summary;
		ErrorDetails = errorDetails;
	}

	public int FrameNumber { get; }

	public long SequenceNumber { get; }

	public string Kind { get; }

	public string Summary { get; }

	public string DisplayText => $"{FrameNumber}  {Kind}  {Summary}";

	public string? ErrorDetails { get; }

	public bool HasError => !string.IsNullOrWhiteSpace(ErrorDetails);

	internal int SourceFrameIndex { get; }

	internal SemanticRecordingFrame SourceFrame { get; }
}
