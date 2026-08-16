namespace DeepFlowTest.Automation;

using System;
using DeepFlowTest.Interop;

public enum WaitConditionKind
{
	Exists,
	Absent,
	ExactCount,
	MinimumCount,
	PropertyEquals,
	PropertyDiffers,
	Enabled,
	Disabled,
	Visible,
	Hidden,
	Stable,
	Responsive,
	WindowTitleChanged,
}

public abstract record WaitCondition(WaitConditionKind Kind);

public abstract record ElementWaitCondition(WaitConditionKind Kind, IWaitTargetMatcher Target)
	: WaitCondition(Kind);

public sealed record ElementExistsWaitCondition(IWaitTargetMatcher Target)
	: ElementWaitCondition(WaitConditionKind.Exists, Target);

public sealed record ElementAbsentWaitCondition(IWaitTargetMatcher Target)
	: ElementWaitCondition(WaitConditionKind.Absent, Target);

public sealed record ElementExactCountWaitCondition(IWaitTargetMatcher Target, int Count)
	: ElementWaitCondition(WaitConditionKind.ExactCount, Target);

public sealed record ElementMinimumCountWaitCondition(IWaitTargetMatcher Target, int Count)
	: ElementWaitCondition(WaitConditionKind.MinimumCount, Target);

public abstract record ElementPropertyWaitCondition(
	WaitConditionKind Kind,
	IWaitTargetMatcher Target,
	string PropertyName,
	string PropertyValue)
	: ElementWaitCondition(Kind, Target);

public sealed record ElementPropertyEqualsWaitCondition(IWaitTargetMatcher Target, string PropertyName, string PropertyValue)
	: ElementPropertyWaitCondition(WaitConditionKind.PropertyEquals, Target, PropertyName, PropertyValue);

public sealed record ElementPropertyDiffersWaitCondition(IWaitTargetMatcher Target, string PropertyName, string PropertyValue)
	: ElementPropertyWaitCondition(WaitConditionKind.PropertyDiffers, Target, PropertyName, PropertyValue);

public sealed record ElementEnabledWaitCondition(IWaitTargetMatcher Target)
	: ElementWaitCondition(WaitConditionKind.Enabled, Target);

public sealed record ElementDisabledWaitCondition(IWaitTargetMatcher Target)
	: ElementWaitCondition(WaitConditionKind.Disabled, Target);

public sealed record ElementVisibleWaitCondition(IWaitTargetMatcher Target)
	: ElementWaitCondition(WaitConditionKind.Visible, Target);

public sealed record ElementHiddenWaitCondition(IWaitTargetMatcher Target)
	: ElementWaitCondition(WaitConditionKind.Hidden, Target);

public sealed record StableWaitCondition(int StabilityMs, IWaitSnapshotFingerprint Fingerprint)
	: WaitCondition(WaitConditionKind.Stable);

public sealed record ResponsiveWaitCondition : WaitCondition
{
	public ResponsiveWaitCondition() : base(WaitConditionKind.Responsive)
	{
	}
}

public sealed record WindowTitleChangedWaitCondition(string? InitialTitle = null)
	: WaitCondition(WaitConditionKind.WindowTitleChanged);

public interface IWaitTargetMatcher
{
	FindResultData Find(VisualTreeSnapshot snapshot);
}

public interface IWaitSnapshotFingerprint
{
	string Compute(VisualTreeSnapshot snapshot);
}

public sealed class FindOptionsWaitTargetMatcher(
	FindSnapshotOptions options,
	FindSnapshotService? findService = null) : IWaitTargetMatcher
{
	private readonly FindSnapshotOptions options = options ?? throw new ArgumentNullException(nameof(options));
	private readonly FindSnapshotService findService = findService ?? new FindSnapshotService();

	public FindResultData Find(VisualTreeSnapshot snapshot) => findService.Find(snapshot, options);
}
