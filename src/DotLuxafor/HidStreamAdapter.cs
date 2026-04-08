using HidSharp;

namespace DotLuxafor;

/// <summary>
/// Production wrapper around <see cref="HidStream"/> implementing <see cref="IHidStreamAdapter"/>.
/// </summary>
internal sealed class HidStreamAdapter : IHidStreamAdapter
{
    private readonly HidStream _stream;

    public HidStreamAdapter(HidStream stream)
    {
        _stream = stream;
    }

    public bool CanWrite => _stream.CanWrite;
    public bool CanRead => _stream.CanRead;

    public int ReadTimeout
    {
        get => _stream.ReadTimeout;
        set => _stream.ReadTimeout = value;
    }

    public void Write(byte[] buffer) => _stream.Write(buffer);
    public int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
    public void SetFeature(byte[] buffer) => _stream.SetFeature(buffer);
    public void GetFeature(byte[] buffer) => _stream.GetFeature(buffer);

    public string? GetProductName()
    {
        try
        {
            return _stream.Device.GetProductName();
        }
        catch
        {
            return null;
        }
    }

    public string? GetDeviceSerialNumber()
    {
        try
        {
            return _stream.Device.GetSerialNumber();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
