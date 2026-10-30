using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MotionLinkVR.Core;

namespace MotionLinkVR.App;

public partial class MainWindow : Window
{
    private readonly MotionProcessor _motionProcessor = new();
    private LanMotionServer? _lanServer;
    private OrientationSample? _latestSample;
    private bool _isPaused;
    private bool _isEmergencyStopped;
    private bool _hasMotionBaseline;
    private MotionPacket? _pendingMotionPacket;
    private int _motionDispatchPending;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ConfigureDefaults();
        Closed += (_, _) => _ = StopServerAsync();
    }

    private void ConfigureDefaults()
    {
        SensitivitySlider.Value = 1.2;
        SmoothingSlider.Value = 0.2;
        DeadzoneSlider.Value = 0.8;
        _motionProcessor.Sensitivity = 1.2;
        _motionProcessor.Smoothing = 0.2;
        _motionProcessor.Deadzone = 0.8;
        _motionProcessor.Enabled = false;
        ControllerUrlText.Text = "Press Start LAN to create a connection";
        CertificateUrlText.Text = "";
    }

    private async void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_lanServer is not null) return;

        try
        {
            _isEmergencyStopped = false;
            _isPaused = false;
            var mobileRoot = Path.Combine(AppContext.BaseDirectory, "mobile");
            _lanServer = await LanMotionServer.StartAsync(
                mobileRoot,
                QueueServerPacket,
                connected => Dispatcher.UIThread.Post(() => HandleConnectionChanged(connected)));
            ControllerUrlText.Text = _lanServer.ControllerUrl;
            CertificateUrlText.Text = _lanServer.CertificateUrl;
            CertificateFingerprintText.Text = _lanServer.CertificateFingerprint;
            PairingCodeText.Text = _lanServer.PairingCode;
            ConnectionStatusText.Text = $"Waiting for phone on { _lanServer.Address }";
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            _lanServer = null;
            ConnectionStatusText.Text = $"Could not start LAN server: {exception.Message}";
        }
    }

    private async void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        await StopServerAsync();
    }

    private async void CopyControllerUrl_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && _lanServer is not null)
        {
            await clipboard.SetTextAsync(_lanServer.ControllerUrl);
        }
    }

    private async Task StopServerAsync()
    {
        _motionProcessor.Enabled = false;
        _hasMotionBaseline = false;
        var server = _lanServer;
        _lanServer = null;
        if (server is not null)
        {
            await server.DisposeAsync();
        }

        if (StartButton is not null)
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            ConnectionStatusText.Text = "Stopped";
            MotionStateText.Text = "Motion inactive";
        }
    }

    private void HandleConnectionChanged(bool connected)
    {
        _hasMotionBaseline = false;
        _motionProcessor.Enabled = false;
        ConnectionStatusText.Text = connected ? "Phone paired; start motion on phone" : "Phone disconnected";
        LatencyText.Text = connected ? "Stream: direct Wi-Fi" : "Stream: disconnected";
        MotionStateText.Text = _motionProcessor.Enabled ? "Motion active" : "Motion inactive";
    }

    private void QueueServerPacket(MotionPacket packet)
    {
        if (packet.Type != "motion")
        {
            Dispatcher.UIThread.Post(() => HandlePacket(packet));
            return;
        }

        Interlocked.Exchange(ref _pendingMotionPacket, packet);
        if (Interlocked.Exchange(ref _motionDispatchPending, 1) == 0)
        {
            Dispatcher.UIThread.Post(DrainLatestMotionPacket);
        }
    }

    private void DrainLatestMotionPacket()
    {
        var packet = Interlocked.Exchange(ref _pendingMotionPacket, null);
        Volatile.Write(ref _motionDispatchPending, 0);
        if (packet is not null)
        {
            HandlePacket(packet);
        }

        if (Volatile.Read(ref _pendingMotionPacket) is not null
            && Interlocked.Exchange(ref _motionDispatchPending, 1) == 0)
        {
            Dispatcher.UIThread.Post(DrainLatestMotionPacket);
        }
    }

    private void HandlePacket(MotionPacket packet)
    {
        switch (packet.Type)
        {
            case "settings":
                SensitivitySlider.Value = Math.Clamp(packet.Sensitivity, 0.1, 5);
                SmoothingSlider.Value = Math.Clamp(packet.Smoothing, 0, 1);
                DeadzoneSlider.Value = Math.Clamp(packet.Deadzone, 0, 10);
                _motionProcessor.Sensitivity = SensitivitySlider.Value;
                _motionProcessor.Smoothing = SmoothingSlider.Value;
                _motionProcessor.Deadzone = DeadzoneSlider.Value;
                return;
            case "pause":
                _isPaused = true;
                _hasMotionBaseline = false;
                _motionProcessor.Enabled = false;
                ConnectionStatusText.Text = "Paused by phone";
                MotionStateText.Text = "Motion inactive";
                return;
            case "resume":
                _isPaused = false;
                _hasMotionBaseline = false;
                _motionProcessor.Enabled = !_isEmergencyStopped && _lanServer is not null;
                ConnectionStatusText.Text = _motionProcessor.Enabled ? "Phone connected" : "Stopped";
                MotionStateText.Text = _motionProcessor.Enabled ? "Motion active" : "Motion inactive";
                return;
            case "recenter":
                _motionProcessor.Recenter(0, 0, 0);
                _hasMotionBaseline = true;
                return;
            case "motion":
                ProcessMotion(packet);
                return;
        }
    }

    private void ProcessMotion(MotionPacket packet)
    {
        if (!_motionProcessor.Enabled) return;
        var packetAge = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - packet.Timestamp;
        if (packet.Timestamp <= 0 || packetAge > 500 || packetAge < -500) return;

        var sample = new OrientationSample(packet.Yaw, packet.Pitch, packet.Roll, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!_hasMotionBaseline)
        {
            _motionProcessor.Recenter(sample.Yaw, sample.Pitch, sample.Roll);
            _hasMotionBaseline = true;
            _latestSample = sample;
            return;
        }

        _motionProcessor.Sensitivity = SensitivitySlider.Value;
        _motionProcessor.Smoothing = SmoothingSlider.Value;
        _motionProcessor.Deadzone = DeadzoneSlider.Value;
        _motionProcessor.InvertX = InvertXCheck.IsChecked ?? false;
        _motionProcessor.InvertY = InvertYCheck.IsChecked ?? false;
        var delta = _motionProcessor.Process(sample);
        _latestSample = sample;
        if (delta.IsValid)
        {
            MouseDriver.MoveRelative((int)Math.Round(delta.X), (int)Math.Round(delta.Y));
        }

        OrientationText.Text = $"Pitch: {sample.Pitch:F2} | Yaw: {sample.Yaw:F2} | Roll: {sample.Roll:F2}";
        MouseText.Text = $"Mouse delta: {delta.X:F2}, {delta.Y:F2}";
    }

    private void RecenterButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_latestSample is { } sample)
        {
            _motionProcessor.Recenter(sample.Yaw, sample.Pitch, sample.Roll);
        }
        _hasMotionBaseline = true;
        OrientationText.Text = "Pitch: 0.00 | Yaw: 0.00 | Roll: 0.00";
    }

    private void PauseButton_Click(object? sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        _hasMotionBaseline = false;
        _motionProcessor.Enabled = !_isPaused && !_isEmergencyStopped && _lanServer is not null;
        ConnectionStatusText.Text = _isPaused ? "Paused" : "Connected";
    }

    private void EmergencyStopButton_Click(object? sender, RoutedEventArgs e)
    {
        _isEmergencyStopped = true;
        _motionProcessor.Enabled = false;
        _isPaused = true;
        MotionStateText.Text = "Emergency stopped";
        ConnectionStatusText.Text = "Emergency stopped";
    }

    private void UpdateMotion(OrientationSample sample)
    {
        ProcessMotion(new MotionPacket("motion", sample.Yaw, sample.Pitch, sample.Roll, sample.TimestampUtc));
    }

    public void ApplySample(double yaw, double pitch, double roll)
    {
        var sample = new OrientationSample(
            AngleMath.NormalizeDegrees(yaw),
            AngleMath.NormalizeDegrees(pitch),
            AngleMath.NormalizeDegrees(roll),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            true);

        UpdateMotion(sample);
    }
}
