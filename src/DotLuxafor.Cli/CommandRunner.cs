namespace DotLuxafor.Cli;

internal sealed class CommandRunner
{
	private readonly ILuxaforDevice _device;

	public CommandRunner(ILuxaforDevice device)
	{
		_device = device;
	}

	public async Task SetColorAsync(ArgParser args)
	{
		LuxaforColor color = ColorParser.Parse(args.GetRequired("color"));
		LedTarget target = ParseTarget(args.GetOptional("target"));
		await _device.SetColorAsync(color, target);
	}

	public async Task FadeAsync(ArgParser args)
	{
		LuxaforColor color = ColorParser.Parse(args.GetRequired("color"));
		byte speed = args.GetRequiredByte("speed");
		LedTarget target = ParseTarget(args.GetOptional("target"));
		await _device.FadeToAsync(color, speed, target);
	}

	public async Task StrobeAsync(ArgParser args)
	{
		LuxaforColor color = ColorParser.Parse(args.GetRequired("color"));
		byte speed = args.GetRequiredByte("speed");
		byte repeat = args.GetRequiredByte("repeat");
		LedTarget target = ParseTarget(args.GetOptional("target"));
		await _device.StrobeAsync(color, speed, repeat, target);
	}

	public async Task WaveAsync(ArgParser args)
	{
		WaveType type = ParseWaveType(args.GetRequired("type"));
		LuxaforColor color = ColorParser.Parse(args.GetRequired("color"));
		byte speed = args.GetRequiredByte("speed");
		byte repeat = args.GetRequiredByte("repeat");
		await _device.WaveAsync(type, color, speed, repeat);
	}

	public async Task PatternAsync(ArgParser args)
	{
		BuiltInPattern pattern = ParsePattern(args.GetRequired("name"));
		byte repeat = args.GetRequiredByte("repeat");
		await _device.PlayPatternAsync(pattern, repeat);
	}

	public async Task TurnOffAsync()
	{
		await _device.TurnOffAsync();
	}

	public async Task InfoAsync()
	{
		await _device.RequestDeviceInfoAsync();

		if (_device.DeviceInfo is { } info)
		{
			Console.WriteLine($"Device type:   {info.Type}");
			Console.WriteLine($"Serial number: {info.SerialNumber}");
		}
		else
		{
			Console.Error.WriteLine("Device did not respond with identification info.");
		}
	}

	private static LedTarget ParseTarget(string? value)
	{
		if (value is null)
		{
			return LedTarget.All;
		}

		return value.ToLowerInvariant() switch
		{
			"all" => LedTarget.All,
			"top" => LedTarget.TopSide,
			"bottom" => LedTarget.BottomSide,
			"led1" => LedTarget.Led1,
			"led2" => LedTarget.Led2,
			"led3" => LedTarget.Led3,
			"led4" => LedTarget.Led4,
			"led5" => LedTarget.Led5,
			"led6" => LedTarget.Led6,
			_ => throw new ArgumentException(
				$"Invalid target: '{value}'. Use: all, top, bottom, led1-led6.")
		};
	}

	private static WaveType ParseWaveType(string value)
	{
		if (Enum.TryParse<WaveType>(value, ignoreCase: true, out var result) && Enum.IsDefined(result))
		{
			return result;
		}

		throw new ArgumentException(
			$"Invalid wave type: '{value}'. Use: short, long, shortoverlapping, longoverlapping, smooth.");
	}

	private static BuiltInPattern ParsePattern(string value)
	{
		if (Enum.TryParse<BuiltInPattern>(value, ignoreCase: true, out var result) && Enum.IsDefined(result))
		{
			return result;
		}

		throw new ArgumentException(
			$"Invalid pattern: '{value}'. Use: trafficlights, random1, random2, random3, random4, random5, police, rainbow.");
	}
}
