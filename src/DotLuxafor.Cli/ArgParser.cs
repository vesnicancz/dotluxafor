namespace DotLuxafor.Cli;

internal sealed class ArgParser
{
	private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

	public ArgParser(string[] args, int startIndex = 1)
	{
		for (int i = startIndex; i < args.Length; i++)
		{
			if (args[i].StartsWith("--", StringComparison.Ordinal))
			{
				string key = args[i][2..];
				if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
				{
					throw new ArgumentException($"Option --{key} requires a value.");
				}

				_options[key] = args[i + 1];
				i++;
			}
		}
	}

	public string GetRequired(string key)
	{
		if (_options.TryGetValue(key, out string? value))
		{
			return value;
		}

		throw new ArgumentException($"Missing required option: --{key}");
	}

	public string? GetOptional(string key, string? defaultValue = null)
	{
		return _options.TryGetValue(key, out string? value) ? value : defaultValue;
	}

	public byte GetRequiredByte(string key)
	{
		string value = GetRequired(key);
		if (byte.TryParse(value, out byte result))
		{
			return result;
		}

		throw new ArgumentException($"Option --{key} must be a number between 0 and 255, got '{value}'");
	}
}
