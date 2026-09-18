namespace DotLuxafor.Cli;

internal static class ColorParser
{
	private static readonly Dictionary<string, LuxaforColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
	{
		["red"] = LuxaforColor.Red,
		["green"] = LuxaforColor.Green,
		["blue"] = LuxaforColor.Blue,
		["yellow"] = LuxaforColor.Yellow,
		["cyan"] = LuxaforColor.Cyan,
		["magenta"] = LuxaforColor.Magenta,
		["white"] = LuxaforColor.White,
		["off"] = LuxaforColor.Off,
	};

	public static LuxaforColor Parse(string input)
	{
		if (NamedColors.TryGetValue(input, out LuxaforColor named))
		{
			return named;
		}

		if (input.Contains(','))
		{
			string[] parts = input.Split(',');
			if (parts.Length == 3 &&
				byte.TryParse(parts[0], out byte r) &&
				byte.TryParse(parts[1], out byte g) &&
				byte.TryParse(parts[2], out byte b))
			{
				return new LuxaforColor(r, g, b);
			}
		}
		// Accepts every spelling the library does, so the CLI is not stricter than the API.
		else if (LuxaforColor.TryFromHex(input, out LuxaforColor hex))
		{
			return hex;
		}

		throw new FormatException(
			$"Invalid color: '{input}'. Use a named color (red, green, blue, yellow, cyan, magenta, white, off), " +
			"hex (#FF0000, FF0000 or #F00), or RGB (255,0,0).");
	}
}
