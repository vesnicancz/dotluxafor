using System.ComponentModel;

namespace DotLuxafor;

/// <summary>
/// Represents an RGB color for the Luxafor device.
/// </summary>
[TypeConverter(typeof(LuxaforColorConverter))]
public readonly record struct LuxaforColor(byte R, byte G, byte B)
#if NET8_0_OR_GREATER
	: IParsable<LuxaforColor>
#endif
{
	/// <summary>Red (255, 0, 0).</summary>
	public static readonly LuxaforColor Red = new(255, 0, 0);

	/// <summary>Green (0, 255, 0).</summary>
	public static readonly LuxaforColor Green = new(0, 255, 0);

	/// <summary>Blue (0, 0, 255).</summary>
	public static readonly LuxaforColor Blue = new(0, 0, 255);

	/// <summary>Yellow (255, 255, 0).</summary>
	public static readonly LuxaforColor Yellow = new(255, 255, 0);

	/// <summary>Cyan (0, 255, 255).</summary>
	public static readonly LuxaforColor Cyan = new(0, 255, 255);

	/// <summary>Magenta (255, 0, 255).</summary>
	public static readonly LuxaforColor Magenta = new(255, 0, 255);

	/// <summary>White (255, 255, 255).</summary>
	public static readonly LuxaforColor White = new(255, 255, 255);

	/// <summary>Off / black (0, 0, 0).</summary>
	public static readonly LuxaforColor Off = new(0, 0, 0);

	/// <summary>
	/// Parses a hex color string into a <see cref="LuxaforColor"/>.
	/// </summary>
	/// <param name="hex">
	/// A hex color: <c>#RRGGBB</c>, <c>#RGB</c>, or either without the leading <c>#</c>.
	/// Case does not matter and surrounding whitespace is ignored.
	/// </param>
	/// <returns>The parsed color.</returns>
	/// <exception cref="FormatException">Thrown when the string is not a valid hex color.</exception>
	public static LuxaforColor FromHex(string hex)
	{
		if (TryFromHex(hex, out var color))
		{
			return color;
		}

		throw new FormatException($"Invalid hex color: '{hex}'. Expected #RRGGBB or #RGB.");
	}

	/// <summary>
	/// Tries to parse a hex color string into a <see cref="LuxaforColor"/>.
	/// </summary>
	/// <param name="hex">
	/// A hex color: <c>#RRGGBB</c>, <c>#RGB</c>, or either without the leading <c>#</c>.
	/// Case does not matter and surrounding whitespace is ignored.
	/// </param>
	/// <param name="color">The parsed color, or default if parsing fails.</param>
	/// <returns><c>true</c> if parsing succeeded.</returns>
	public static bool TryFromHex(string? hex, out LuxaforColor color)
	{
		color = default;

		if (hex is null)
		{
			return false;
		}

		var text = hex.Trim();
		var start = text.Length > 0 && text[0] == '#' ? 1 : 0;

		switch (text.Length - start)
		{
			case 6:
				if (TryParseByte(text, start, out var r6) &&
					TryParseByte(text, start + 2, out var g6) &&
					TryParseByte(text, start + 4, out var b6))
				{
					color = new LuxaforColor(r6, g6, b6);
					return true;
				}

				return false;

			case 3:
				// #RGB is shorthand for #RRGGBB, so each digit is doubled: F -> FF, which is n * 17.
				if (TryParseDigit(text[start], out var r3) &&
					TryParseDigit(text[start + 1], out var g3) &&
					TryParseDigit(text[start + 2], out var b3))
				{
					color = new LuxaforColor((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
					return true;
				}

				return false;

			default:
				return false;
		}
	}

	/// <summary>
	/// Returns this color scaled towards black — a plain per-channel multiply, not a
	/// perceptual or gamma-corrected adjustment.
	/// </summary>
	/// <param name="factor">
	/// Scale factor: <c>1.0</c> leaves the color unchanged and <c>0.0</c> turns it off.
	/// Values outside 0.0–1.0 are clamped, so animation code need not range-check.
	/// </param>
	public LuxaforColor WithBrightness(double factor)
	{
		factor = Clamp01(factor);
		return new LuxaforColor(Scale(R, factor), Scale(G, factor), Scale(B, factor));
	}

	/// <summary>
	/// Linearly interpolates between two colors, per channel.
	/// </summary>
	/// <param name="from">The color at <paramref name="amount"/> <c>0.0</c>.</param>
	/// <param name="to">The color at <paramref name="amount"/> <c>1.0</c>.</param>
	/// <param name="amount">Position between the two. Values outside 0.0–1.0 are clamped.</param>
	public static LuxaforColor Lerp(LuxaforColor from, LuxaforColor to, double amount)
	{
		amount = Clamp01(amount);
		return new LuxaforColor(
			Mix(from.R, to.R, amount),
			Mix(from.G, to.G, amount),
			Mix(from.B, to.B, amount));
	}

	/// <summary>
	/// Returns the color as a hex string (e.g. "#FF0000").
	/// </summary>
	public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

	/// <inheritdoc />
	public override string ToString() => ToHex();

#if NET8_0_OR_GREATER
	/// <summary>
	/// Parses a hex color string. Equivalent to <see cref="FromHex"/>; the provider is ignored,
	/// because a hex color does not vary by culture.
	/// </summary>
	public static LuxaforColor Parse(string s, IFormatProvider? provider) => FromHex(s);

	/// <summary>
	/// Tries to parse a hex color string. Equivalent to <see cref="TryFromHex"/>; the provider is
	/// ignored, because a hex color does not vary by culture.
	/// </summary>
	public static bool TryParse(string? s, IFormatProvider? provider, out LuxaforColor result)
		=> TryFromHex(s, out result);
#endif

	private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

	private static byte Scale(byte channel, double factor)
		=> (byte)Math.Round(channel * factor, MidpointRounding.AwayFromZero);

	private static byte Mix(byte from, byte to, double amount)
		=> (byte)Math.Round(from + ((to - from) * amount), MidpointRounding.AwayFromZero);

	private static bool TryParseByte(string text, int index, out byte value)
	{
		value = 0;

		if (!TryParseDigit(text[index], out var high) || !TryParseDigit(text[index + 1], out var low))
		{
			return false;
		}

		value = (byte)((high << 4) | low);
		return true;
	}

	private static bool TryParseDigit(char c, out int value)
	{
		if (c >= '0' && c <= '9')
		{
			value = c - '0';
			return true;
		}

		if (c >= 'a' && c <= 'f')
		{
			value = c - 'a' + 10;
			return true;
		}

		if (c >= 'A' && c <= 'F')
		{
			value = c - 'A' + 10;
			return true;
		}

		value = 0;
		return false;
	}
}
