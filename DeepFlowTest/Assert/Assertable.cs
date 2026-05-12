namespace DeepFlowTest.Assert;

using System;
using System.Linq.Expressions;
using System.Threading;
using DeepFlowTest.Assert.TestFrameworks;

public sealed class Assertable
{
	private readonly Expression valueExpression;
	private readonly Action onCheck;

	private Assertable(Element value, Expression valueExpression, Action onCheck)
	{
		Value = value ?? throw new ArgumentNullException(nameof(value));
		this.valueExpression = valueExpression ?? throw new ArgumentNullException(nameof(valueExpression));
		this.onCheck = onCheck ?? throw new ArgumentNullException(nameof(onCheck));
	}

	public Element Value { get; }

	public Assertable IsTrue(Expression<Func<Element, bool?>> predicateExpression, int timeoutMs = 5_000)
	{
		_ = predicateExpression ?? throw new ArgumentNullException(nameof(predicateExpression));

		var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1, timeoutMs));
		Exception? lastException = null;
		do
		{
			try
			{
				onCheck();
				if (predicateExpression.Compile().Invoke(Value).GetValueOrDefault())
					return this;
			}
			catch (Exception ex)
			{
				lastException = ex;
			}

			if (DateTime.UtcNow < deadline)
				Thread.Sleep(Math.Min(100, Math.Max(1, timeoutMs)));
		}
		while (DateTime.UtcNow < deadline);

		TestFrameworkProvider.Throw(ElementAssertExtensions.GetDiagnosticMessage(predicateExpression.Body, Value, valueExpression, lastException));
		return this;
	}

	public static implicit operator Element(Assertable source) =>
		source?.Value ?? throw new ArgumentNullException(nameof(source));

	internal static Assertable FromValueExpression(Element value, Expression valueExpression, Action onCheck) =>
		new(value, valueExpression, onCheck);
}
