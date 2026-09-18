using System.Reflection;
using DotLuxafor;
using Moq;

namespace DotLuxafor.Tests;

public class LuxaforDeviceDisconnectedExceptionTests
{
    private static readonly LuxaforDeviceDescriptor Descriptor =
        new LuxaforDeviceDescriptor("/dev/hidraw0", "LUXAFOR FLAG", "1001");

    /// <summary>
    /// Guards the public surface rather than any behaviour. This assembly sees the library's
    /// internals, so every other test here would compile with the constructor internal — which is
    /// the state that left a consumer unable to build the exception the library actually throws,
    /// and so unable to test the handling the README tells them to write.
    /// </summary>
    [Fact]
    public void DescriptorConstructor_IsCallableFromOutsideTheLibrary()
    {
        var constructor = typeof(LuxaforDeviceDisconnectedException).GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            new[] { typeof(LuxaforDeviceDescriptor), typeof(Exception) },
            modifiers: null);

        Assert.NotNull(constructor);
    }

    [Fact]
    public void DescriptorConstructor_CarriesTheDeviceAndTheReason()
    {
        var error = new IOException("The device was disconnected.");

        var ex = new LuxaforDeviceDisconnectedException(Descriptor, error);

        Assert.Same(Descriptor, ex.Descriptor);
        Assert.Same(error, ex.InnerException);
        Assert.Contains("LUXAFOR FLAG (1001)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DescriptorConstructor_WithoutADescriptor_FallsBackToTheDefaultMessage()
    {
        // Named argument on purpose: a bare (null, null) is ambiguous with the (string, Exception)
        // overload, and this is the spelling that says which one is meant.
        var ex = new LuxaforDeviceDisconnectedException(descriptor: null, innerException: null);

        Assert.Null(ex.Descriptor);
        Assert.Null(ex.InnerException);
        Assert.Contains("no longer connected", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of making it public: an exception a test builds has to be the same thing the
    /// library throws, or the handling it exercises is not the handling that will run.
    /// </summary>
    [Fact]
    public async Task ConstructedException_MatchesOneTheLibraryThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var error = new IOException("The device was disconnected.");
        var stream = new Mock<IHidStreamAdapter>();
        stream.Setup(s => s.CanWrite).Returns(true);
        stream.Setup(s => s.Write(It.IsAny<byte[]>())).Throws(error);
        using var device = new LuxaforDevice(stream.Object, Descriptor);

        var thrown = await Assert.ThrowsAsync<LuxaforDeviceDisconnectedException>(() =>
            device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct));
        var built = new LuxaforDeviceDisconnectedException(Descriptor, error);

        Assert.Equal(thrown.Message, built.Message);
        Assert.Equal(thrown.Descriptor, built.Descriptor);
        Assert.Same(thrown.InnerException, built.InnerException);
    }

    [Fact]
    public void StillDerivesFromInvalidOperationException()
    {
        // Code written before the type existed catches InvalidOperationException to mean "the
        // device went away", and it has to keep working.
        Assert.IsAssignableFrom<InvalidOperationException>(new LuxaforDeviceDisconnectedException(Descriptor, null));
    }
}
