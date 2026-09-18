using System.Globalization;
using System.Runtime.InteropServices;

namespace DotLuxafor;

/// <summary>
/// Turns the exception HidSharp reports for a failed open into a <see cref="DeviceOpenStatus"/>
/// and a message the caller can show to a user.
/// </summary>
/// <remarks>
/// HidSharp does not expose a typed reason, and each backend reports failures differently:
/// Windows puts the Win32 code in <see cref="Exception.HResult"/>, Linux throws
/// <see cref="UnauthorizedAccessException"/> for EACCES, and macOS only names the IOKit
/// code in the message text. The classification below covers all three.
/// </remarks>
internal static class HidOpenFailure
{
	// Win32 codes, surfaced through IOException.HResult by the Windows backend.
	private const int ErrorAccessDenied = unchecked((int)0x80070005);
	private const int ErrorSharingViolation = unchecked((int)0x80070020);

	// IOKit codes. The macOS backend formats them into the message as the name of
	// HidSharp's IOReturn enum ("NotPermitted"), or as a signed decimal for values
	// that enum does not name.
	private const int IOReturnNotPrivileged = unchecked((int)0xE00002C1);
	private const int IOReturnExclusiveAccess = unchecked((int)0xE00002C5);
	private const int IOReturnNotPermitted = unchecked((int)0xE00002E2);

	private const string MacErrorPrefix = "Unable to open HID class device (error ";

	/// <summary>
	/// Classifies the exception from a failed open. Returns <see cref="DeviceOpenStatus.Failed"/>
	/// when the reason cannot be determined.
	/// </summary>
	internal static DeviceOpenStatus Classify(Exception? error)
	{
		switch (error)
		{
			case null:
				return DeviceOpenStatus.Failed;

			// Linux reports a missing udev rule (EACCES on /dev/hidraw*) this way.
			case UnauthorizedAccessException:
				return DeviceOpenStatus.AccessDenied;

			case IOException io:
				return ClassifyIOException(io);

			default:
				return DeviceOpenStatus.Failed;
		}
	}

	/// <summary>
	/// Builds a human-readable explanation, including a platform-specific hint for failures
	/// that a user can act on.
	/// </summary>
	internal static string Describe(DeviceOpenStatus status, Exception? error)
	{
		string text = status switch
		{
			DeviceOpenStatus.Opened => "Luxafor device opened.",
			DeviceOpenStatus.NotFound => string.Format(
				CultureInfo.InvariantCulture,
				"No Luxafor device found (VID 0x{0:X4}, PID 0x{1:X4}).",
				LuxaforDevice.VendorId,
				LuxaforDevice.ProductId),
			DeviceOpenStatus.AccessDenied => "A Luxafor device is connected, but the operating system denied access to it. " + PermissionHint(),
			DeviceOpenStatus.InUse => "A Luxafor device is connected, but another application is holding it open.",
			_ => "A Luxafor device is connected, but it could not be opened. " + PermissionHint()
		};

		return error == null ? text : text + " (" + error.Message + ")";
	}

	private static DeviceOpenStatus ClassifyIOException(IOException error)
	{
		switch (error.HResult)
		{
			case ErrorAccessDenied:
				return DeviceOpenStatus.AccessDenied;

			case ErrorSharingViolation:
				return DeviceOpenStatus.InUse;

			default:
				return ClassifyMacErrorCode(error.Message);
		}
	}

	private static DeviceOpenStatus ClassifyMacErrorCode(string? message)
	{
		if (message == null)
		{
			return DeviceOpenStatus.Failed;
		}

		int start = message.IndexOf(MacErrorPrefix, StringComparison.Ordinal);
		if (start < 0)
		{
			return DeviceOpenStatus.Failed;
		}

		start += MacErrorPrefix.Length;
		int end = message.IndexOf(')', start);
		if (end < 0)
		{
			return DeviceOpenStatus.Failed;
		}

		string code = message.Substring(start, end - start);

		if (int.TryParse(code, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int numeric))
		{
			return numeric switch
			{
				IOReturnNotPermitted or IOReturnNotPrivileged => DeviceOpenStatus.AccessDenied,
				IOReturnExclusiveAccess => DeviceOpenStatus.InUse,
				_ => DeviceOpenStatus.Failed
			};
		}

		return code switch
		{
			"NotPermitted" or "NotPrivileged" => DeviceOpenStatus.AccessDenied,
			"ExclusiveAccess" => DeviceOpenStatus.InUse,
			_ => DeviceOpenStatus.Failed
		};
	}

	private static string PermissionHint()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			return "On macOS, HID access requires the Input Monitoring permission "
				+ "(System Settings > Privacy & Security > Input Monitoring), and App Sandbox blocks IOKit HID access entirely.";
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			return "On Linux, /dev/hidraw* nodes are root-only by default; add a udev rule for 04d8:f372.";
		}

		return "Another application, such as the official Luxafor software, may be holding the device open.";
	}
}
