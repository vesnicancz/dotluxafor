using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotLuxafor;
using DotLuxafor.ControlPanel.Models;
using DotLuxafor.ControlPanel.Services;

namespace DotLuxafor.ControlPanel.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly DeviceService _deviceService;
    private bool _suppressHexSync;
    private bool _suppressRgbSync;

    // === Device ===
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _deviceType = "—";
    [ObservableProperty] private string _serialNumber = "—";
    [ObservableProperty] private string _batteryInfo = "—";
    [ObservableProperty] private string _rssiInfo = "—";

    // === Color ===
    [ObservableProperty] private double _red = 255;
    [ObservableProperty] private double _green;
    [ObservableProperty] private double _blue;
    [ObservableProperty] private string _hexColor = "#FF0000";

    // === LED Target ===
    [ObservableProperty] private int _selectedLedIndex; // 0=All, 1=Top, 2=Bottom, 3-8=Led1-6

    // === Fade ===
    [ObservableProperty] private double _fadeSpeed = 128;

    // === Strobe ===
    [ObservableProperty] private double _strobeSpeed = 128;
    [ObservableProperty] private double _strobeRepeat = 5;

    // === Wave ===
    [ObservableProperty] private WaveType _selectedWaveType = WaveType.Short;
    [ObservableProperty] private double _waveSpeed = 128;
    [ObservableProperty] private double _waveRepeat = 3;

    // === Pattern ===
    [ObservableProperty] private BuiltInPattern _selectedPattern = BuiltInPattern.Rainbow;
    [ObservableProperty] private double _patternRepeat = 3;

    // === Event Log ===
    public ObservableCollection<EventLogEntry> EventLog { get; } = [];

    // === Enum sources for ComboBox binding ===
    public WaveType[] WaveTypes { get; } = Enum.GetValues<WaveType>();
    public BuiltInPattern[] BuiltInPatterns { get; } = Enum.GetValues<BuiltInPattern>();

    public MainViewModel(DeviceService deviceService)
    {
        _deviceService = deviceService;
        _deviceService.LogMessage += OnLogMessage;
        _deviceService.StateChanged += OnDeviceStateChanged;
    }

    // === RGB <-> Hex sync ===

    partial void OnRedChanged(double value) => SyncHexFromRgb();
    partial void OnGreenChanged(double value) => SyncHexFromRgb();
    partial void OnBlueChanged(double value) => SyncHexFromRgb();

    partial void OnHexColorChanged(string value)
    {
        if (_suppressHexSync) return;

        if (LuxaforColor.TryFromHex(value, out var color))
        {
            _suppressRgbSync = true;
            Red = color.R;
            Green = color.G;
            Blue = color.B;
            _suppressRgbSync = false;
        }
    }

    private void SyncHexFromRgb()
    {
        if (_suppressRgbSync) return;

        _suppressHexSync = true;
        HexColor = $"#{(byte)Red:X2}{(byte)Green:X2}{(byte)Blue:X2}";
        _suppressHexSync = false;
    }

    // === LED target mapping ===

    private LedTarget GetSelectedLedTarget() => SelectedLedIndex switch
    {
        0 => LedTarget.All,
        1 => LedTarget.TopSide,
        2 => LedTarget.BottomSide,
        3 => LedTarget.Led1,
        4 => LedTarget.Led2,
        5 => LedTarget.Led3,
        6 => LedTarget.Led4,
        7 => LedTarget.Led5,
        8 => LedTarget.Led6,
        _ => LedTarget.All
    };

    // === Device commands ===

    [RelayCommand]
    private void Connect()
    {
        _deviceService.Connect();
    }

    [RelayCommand]
    private void Disconnect()
    {
        _deviceService.Disconnect();
    }

    [RelayCommand]
    private void TurnOff()
    {
        _deviceService.TurnOff();
    }

    // === Color commands ===

    [RelayCommand]
    private void SetColor()
    {
        _deviceService.SetColor(GetSelectedLedTarget(), (byte)Red, (byte)Green, (byte)Blue);
    }

    [RelayCommand]
    private void ApplyPreset(string hex)
    {
        if (LuxaforColor.TryFromHex(hex, out var color))
        {
            Red = color.R;
            Green = color.G;
            Blue = color.B;
            _deviceService.SetColor(GetSelectedLedTarget(), color.R, color.G, color.B);
        }
    }

    // === Effect commands ===

    [RelayCommand]
    private void ApplyFade()
    {
        _deviceService.FadeTo(GetSelectedLedTarget(), (byte)Red, (byte)Green, (byte)Blue, (byte)FadeSpeed);
    }

    [RelayCommand]
    private void ApplyStrobe()
    {
        _deviceService.Strobe(GetSelectedLedTarget(), (byte)Red, (byte)Green, (byte)Blue, (byte)StrobeSpeed, (byte)StrobeRepeat);
    }

    [RelayCommand]
    private void ApplyWave()
    {
        _deviceService.Wave(SelectedWaveType, (byte)Red, (byte)Green, (byte)Blue, (byte)WaveSpeed, (byte)WaveRepeat);
    }

    [RelayCommand]
    private void ApplyPattern()
    {
        _deviceService.PlayPattern(SelectedPattern, (byte)PatternRepeat);
    }

    [RelayCommand]
    private void ClearLog()
    {
        EventLog.Clear();
    }

    // === Event handlers ===

    private void OnLogMessage(string message)
    {
        EventLog.Insert(0, new EventLogEntry(DateTime.Now, message));

        // Cap at 100 entries
        while (EventLog.Count > 100)
            EventLog.RemoveAt(EventLog.Count - 1);
    }

    private void OnDeviceStateChanged()
    {
        IsConnected = _deviceService.IsConnected;

        if (_deviceService.DeviceInfo is { } info)
        {
            DeviceType = info.Type.ToString();
            SerialNumber = info.SerialNumber.ToString();
        }
        else
        {
            DeviceType = "—";
            SerialNumber = "—";
        }

        if (_deviceService.DongleInfo is { } dongle)
        {
            BatteryInfo = $"{dongle.BatteryLevel}% {dongle.BatteryStatus}";
            RssiInfo = $"{dongle.Rssi} dBm";
        }
        else
        {
            BatteryInfo = "—";
            RssiInfo = "—";
        }
    }

    public void Dispose()
    {
        _deviceService.LogMessage -= OnLogMessage;
        _deviceService.StateChanged -= OnDeviceStateChanged;
        _deviceService.Dispose();
    }
}
