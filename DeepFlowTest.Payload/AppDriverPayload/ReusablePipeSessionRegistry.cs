namespace DeepFlowTest.AppDriverPayload;

using System;
using System.Collections.Concurrent;

public static class ReusablePipeSessionRegistry
{
	private static readonly ConcurrentDictionary<string, ReusablePipeSession> Sessions = new();

	public static ReusablePipeSession GetOrStart(string pipeName)
	{
		var session = Sessions.GetOrAdd(pipeName, name => new ReusablePipeSession(name));
		session.Start();
		return session;
	}

	public static ReusablePipeSession GetOrStart(string pipeName, Action<ReusablePipeSession> runCommandLoop)
	{
		var session = Sessions.GetOrAdd(pipeName, name => new ReusablePipeSession(name, runCommandLoop));
		session.Start();
		return session;
	}

	public static bool TryGet(string pipeName, out ReusablePipeSession? session)
	{
		return Sessions.TryGetValue(pipeName, out session);
	}

	public static int Count => Sessions.Count;

	public static void ClearForTests()
	{
		foreach (var session in Sessions.Values)
			session.Stop();

		Sessions.Clear();
	}
}
