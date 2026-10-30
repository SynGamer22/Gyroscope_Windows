using Avalonia.Controls;
using Avalonia.Interactivity;
using MotionLinkVR.Core;

namespace MotionLinkVR.App;

public partial class MainWindow : Window
{
    private readonly MotionProcessor _motionProcessor = new();
    private bool _isPaused;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ConfigureDefaults();
    }

    private void ConfigureDefaults()
    {
        SensitivitySlider.Value = 1.2;
        SmoothingSlider.Value = 0.2;
        DeadzoneSlider.Value = 0.8;
        _motionProcessor.Sensitivity = 1.2;
        _motionProcessor.Smoothing = 0.2;
        _motionProcessor.Deadzone = 0.8;
        _motionProcessor.Enabled = true;
    }

    private void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        _motionProcessor.Enabled = true;
        _isPaused = false;
        ConnectionStatusText.Text = "Connected";
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        _motionProcessor.Enabled = false;
        ConnectionStatusText.Text = "Stopped";
    }

    private void RecenterButton_Click(object? sender, RoutedEventArgs e)
    {
        _motionProcessor.Recenter(0d, 0d, 0d);
        OrientationText.Text = "Pitch: 0.00 | Yaw: 0.00 | Roll: 0.00";
    }

    private void PauseButton_Click(object? sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        _motionProcessor.Enabled = !_isPaused;
        ConnectionStatusText.Text = _isPaused ? "Paused" : "Connected";
    }

    private void EmergencyStopButton_Click(object? sender, RoutedEventArgs e)
    {
        _motionProcessor.Enabled = false;
        _isPaused = true;
        ConnectionStatusText.Text = "Emergency stopped";
    }

    private void UpdateMotion(OrientationSample sample)
    {
        var delta = _motionProcessor.Process(sample);
        _motionProcessor.Sensitivity = SensitivitySlider.Value;
        _motionProcessor.Smoothing = SmoothingSlider.Value;
        _motionProcessor.Deadzone = DeadzoneSlider.Value;
        _motionProcessor.InvertX = InvertXCheck.IsChecked ?? false;
        _motionProcessor.InvertY = InvertYCheck.IsChecked ?? false;

        MouseDriver.MoveRelative((int)Math.Round(delta.X), (int)Math.Round(delta.Y));
        OrientationText.Text = $"Pitch: {sample.Pitch:F2} | Yaw: {sample.Yaw:F2} | Roll: {sample.Roll:F2}";
        MouseText.Text = $"Mouse delta: {delta.X:F2}, {delta.Y:F2}";
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
