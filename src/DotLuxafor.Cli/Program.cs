namespace DotLuxafor.Cli;

internal static class Program
{
	private static async Task<int> Main(string[] args)
	{
		if (args.Length == 0 || args[0] is "--help" or "-h")
		{
			PrintUsage();
			return 0;
		}

		try
		{
			return await RunAsync(args);
		}
		catch (Exception ex) when (ex is ArgumentException or FormatException)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return 1;
		}
		catch (OperationCanceledException)
		{
			Console.Error.WriteLine("Error: Operation cancelled.");
			return 3;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return 3;
		}
	}

	private static readonly HashSet<string> DeviceCommands = new(StringComparer.OrdinalIgnoreCase)
	{
		"set-color", "fade", "strobe", "wave", "pattern", "off", "info"
	};

	private static async Task<int> RunAsync(string[] args)
	{
		if (!DeviceCommands.Contains(args[0]))
		{
			Console.Error.WriteLine($"Error: Unknown command: '{args[0]}'");
			PrintUsage();
			return 1;
		}

		string command = args[0].ToLowerInvariant();

		using ILuxaforDevice? device = LuxaforDevices.TryOpen();
		if (device is null)
		{
			Console.Error.WriteLine("Error: No Luxafor device found.");
			return 2;
		}

		var parser = new ArgParser(args);
		var runner = new CommandRunner(device);

		switch (command)
		{
			case "set-color":
				await runner.SetColorAsync(parser);
				break;
			case "fade":
				await runner.FadeAsync(parser);
				break;
			case "strobe":
				await runner.StrobeAsync(parser);
				break;
			case "wave":
				await runner.WaveAsync(parser);
				break;
			case "pattern":
				await runner.PatternAsync(parser);
				break;
			case "off":
				await runner.TurnOffAsync();
				break;
			case "info":
				await runner.InfoAsync();
				break;
		}

		return 0;
	}

	private static void PrintUsage()
	{
		Console.WriteLine("""
			Usage: luxafor <command> [options]

			Commands:
			  set-color  --color <color> [--target <target>]
			  fade       --color <color> --speed <0-255> [--target <target>]
			  strobe     --color <color> --speed <0-255> --repeat <0-255> [--target <target>]
			  wave       --type <type> --color <color> --speed <0-255> --repeat <0-255>
			  pattern    --name <pattern> --repeat <0-255>
			  off
			  info

			Color formats:
			  Named:  red, green, blue, yellow, cyan, magenta, white, off
			  Hex:    #FF0000
			  RGB:    255,0,0

			Targets: all (default), top, bottom, led1-led6
			Wave types: short, long, shortoverlapping, longoverlapping, smooth
			Patterns: trafficlights, random1-5, police, rainbow
			""");
	}
}
