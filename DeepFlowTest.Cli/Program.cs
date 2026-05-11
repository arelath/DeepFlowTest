namespace DeepFlowTest.Cli;

using System.CommandLine;
using System.Linq;

public static class Program
{
	public static int Main(string[] args)
	{
		return Run(args);
	}

	public static int Run(string[] args)
	{
		var root = CreateRootCommand();
		var parseResult = root.Parse(args, new ParserConfiguration());
		if (parseResult.Errors.Count != 0)
			return 2;

		return parseResult.Invoke(new InvocationConfiguration());
	}

	public static RootCommand CreateRootCommand()
	{
		var root = new RootCommand("Drive DeepFlowTest automation workflows.");
		root.SetAction(_ => 0);

		var version = new Command("version", "Print the product name.");
		version.SetAction(_ =>
		{
			System.Console.WriteLine(DeepFlowTest.ProductInfo.Name);
			return 0;
		});
		root.Add(version);

		return root;
	}

	public static bool IsHelpRequest(string[] args)
	{
		return args.Length == 0 || args.Any(static x => x == "--help" || x == "-h" || x == "/?");
	}
}
