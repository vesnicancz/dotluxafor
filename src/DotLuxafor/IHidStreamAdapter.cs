namespace DotLuxafor;

/// <summary>
/// Internal abstraction over a HID device stream for testability.
/// Production code uses <see cref="HidStreamAdapter"/>; tests supply a mock.
/// </summary>
internal interface IHidStreamAdapter : IDisposable
{
    bool CanWrite { get; }
    bool CanRead { get; }
    int ReadTimeout { get; set; }
    void Write(byte[] buffer);
    int Read(byte[] buffer, int offset, int count);
    void SetFeature(byte[] buffer);
    void GetFeature(byte[] buffer);
    string? GetProductName();
    string? GetDeviceSerialNumber();
}
