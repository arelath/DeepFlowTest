namespace DeepFlowTest.Interop;

using System.Linq.Expressions;

public sealed class Eval
{
	public Eval(string expressionJson)
	{
		ExpressionJson = expressionJson;
	}

	private Eval(LambdaExpression expression)
	{
		ExpressionJson = ExpressionPayloadSerializer.SerializeText(expression);
	}

	public static Eval SerializeCode(LambdaExpression expression) =>
		new(expression);

	public string Type { get; } = EvalType;

	public string ExpressionJson { get; }

	public const string EvalType = "p:Eval";
}
