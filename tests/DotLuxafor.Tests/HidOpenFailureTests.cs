using DotLuxafor;

namespace DotLuxafor.Tests;

/// <summary>
/// Covers the mapping from the exception HidSharp reports for a failed open onto
/// <see cref="DeviceOpenStatus"/>. The exception shapes below are the ones the
/// HidSharp 2.6.4 backends actually produce.
/// </summary>
public class HidOpenFailureTests
{
	private const int ErrorAccessDenied = unchecked((int)0x80070005);
	private const int ErrorSharingViolation = unchecked((int)0x80070020);

	[Fact]
	public void Classify_NoException_ReturnsFailed()
	{
		Assert.Equal(DeviceOpenStatus.Failed, HidOpenFailure.Classify(null));
	}

	[Fact]
	public void Classify_UnauthorizedAccess_ReturnsAccessDenied()
	{
		// Linux: HidSharp maps EACCES on /dev/hidraw* to this.
		var error = new UnauthorizedAccessException("Not permitted to open /dev/hidraw0.");

		Assert.Equal(DeviceOpenStatus.AccessDenied, HidOpenFailure.Classify(error));
	}

	[Fact]
	public void Classify_WindowsAccessDeniedHResult_ReturnsAccessDenied()
	{
		var error = new IOException("Unable to open HID class device.", ErrorAccessDenied);

		Assert.Equal(DeviceOpenStatus.AccessDenied, HidOpenFailure.Classify(error));
	}

	[Fact]
	public void Classify_SharingViolationHResult_ReturnsInUse()
	{
		// HidSharp's sharing-violation helper produces exactly this.
		var error = new IOException("The device is in use.", ErrorSharingViolation);

		Assert.Equal(DeviceOpenStatus.InUse, HidOpenFailure.Classify(error));
	}

	[Theory]
	// macOS names the IOKit code when its IOReturn enum has a member for it...
	[InlineData("NotPermitted", DeviceOpenStatus.AccessDenied)]
	[InlineData("NotPrivileged", DeviceOpenStatus.AccessDenied)]
	[InlineData("ExclusiveAccess", DeviceOpenStatus.InUse)]
	// ...and falls back to a signed decimal when it does not.
	[InlineData("-536870174", DeviceOpenStatus.AccessDenied)]
	[InlineData("-536870207", DeviceOpenStatus.AccessDenied)]
	[InlineData("-536870203", DeviceOpenStatus.InUse)]
	[InlineData("Offline", DeviceOpenStatus.Failed)]
	[InlineData("-1234", DeviceOpenStatus.Failed)]
	public void Classify_MacOsErrorCode_IsRecognized(string code, DeviceOpenStatus expected)
	{
		var error = new IOException($"Unable to open HID class device (error {code}): IOService:/Luxafor");

		Assert.Equal(expected, HidOpenFailure.Classify(error));
	}

	[Fact]
	public void Classify_UnrecognizedIOException_ReturnsFailed()
	{
		var error = new IOException("Something else went wrong.");

		Assert.Equal(DeviceOpenStatus.Failed, HidOpenFailure.Classify(error));
	}

	[Fact]
	public void Classify_UnrelatedException_ReturnsFailed()
	{
		Assert.Equal(DeviceOpenStatus.Failed, HidOpenFailure.Classify(new InvalidOperationException()));
	}

	[Fact]
	public void Describe_NotFound_NamesVendorAndProductId()
	{
		string description = HidOpenFailure.Describe(DeviceOpenStatus.NotFound, null);

		Assert.Contains("0x04D8", description, StringComparison.Ordinal);
		Assert.Contains("0xF372", description, StringComparison.Ordinal);
	}

	[Fact]
	public void Describe_AccessDenied_IncludesUnderlyingError()
	{
		string description = HidOpenFailure.Describe(
			DeviceOpenStatus.AccessDenied,
			new IOException("Unable to open HID class device (error NotPermitted): IOService:/Luxafor"));

		Assert.Contains("denied access", description, StringComparison.Ordinal);
		Assert.Contains("NotPermitted", description, StringComparison.Ordinal);
	}

	[Fact]
	public void Describe_InUse_DoesNotClaimAPermissionProblem()
	{
		string description = HidOpenFailure.Describe(DeviceOpenStatus.InUse, null);

		Assert.Contains("another application", description, StringComparison.Ordinal);
		Assert.DoesNotContain("denied access", description, StringComparison.Ordinal);
	}
}
