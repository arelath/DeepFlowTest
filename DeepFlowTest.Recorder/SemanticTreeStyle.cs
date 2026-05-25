namespace DeepFlowTest.Recorder;

public sealed class SemanticTreeStyle
{
	public SemanticTreeStyle(string marker)
	{
		Marker = marker;
	}

	public string Marker { get; }

	public static SemanticTreeStyle For(SemanticRecordingChangeKind changeKind) =>
		changeKind switch
		{
			SemanticRecordingChangeKind.Removed => new SemanticTreeStyle("-"),
			SemanticRecordingChangeKind.Added => new SemanticTreeStyle("+"),
			SemanticRecordingChangeKind.Changed => new SemanticTreeStyle("*"),
			_ => new SemanticTreeStyle(string.Empty),
		};
}
