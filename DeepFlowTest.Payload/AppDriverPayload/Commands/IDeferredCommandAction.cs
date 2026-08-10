namespace DeepFlowTest.AppDriverPayload.Commands;

using System.Threading;
using System.Threading.Tasks;

internal interface IDeferredCommandAction
{
	Task<object> ExecuteAsync(CancellationToken cancellationToken);
}
