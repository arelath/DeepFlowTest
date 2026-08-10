namespace DeepFlowTest.Assert;

using System;
using System.Linq.Expressions;
using System.Threading;
using DeepFlowTest.Assert.TestFrameworks;
using DeepFlowTest.Contracts;

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

	public Assertable IsTrue(Expression<Func<Element, bool?>> predicateExpression, TimeSpan? timeout = null)
	{
		_ = predicateExpression ?? throw new ArgumentNullException(nameof(predicateExpression));
		var effectiveTimeout = timeout ?? TimeSpan.FromMilliseconds(TimeoutDefaults.AssertionTimeoutMs);
		_ = DurationUtility.ToMilliseconds(effectiveTimeout, nameof(timeout));

		var deadline = DateTime.UtcNow.Add(effectiveTimeout);
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
			{
				var remaining = deadline - DateTime.UtcNow;
				Thread.Sleep(Math.Min(TimeoutDefaults.AssertionPollDelayMs, Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds))));
			}
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
