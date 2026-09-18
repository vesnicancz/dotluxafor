using System.ComponentModel;
using System.Globalization;

namespace DotLuxafor;

/// <summary>
/// Converts <see cref="LuxaforColor"/> from its text forms, and to its hex string form.
/// </summary>
/// <remarks>
/// This is what lets a color be bound straight out of configuration — <c>IConfiguration</c> binding
/// goes through <see cref="TypeConverter"/>, not <c>IParsable</c> — so <c>"Color": "#FF8800"</c> in
/// appsettings.json binds to a <see cref="LuxaforColor"/> option. It accepts every spelling
/// <see cref="LuxaforColor.Parse(string)"/> does, so <c>"Color": "red"</c> binds too.
/// </remarks>
internal sealed class LuxaforColorConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
		=> sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

	public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
		=> value is string text ? LuxaforColor.Parse(text) : base.ConvertFrom(context, culture, value);

	public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
		=> destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

	public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
		=> destinationType == typeof(string) && value is LuxaforColor color
			? color.ToHex()
			: base.ConvertTo(context, culture, value, destinationType);
}
