using System.Windows;
using System.Windows.Controls;
using ControllerLabPro.Services;

namespace ControllerLabPro;

public partial class Ps5ElgatoWindow : Window
{
    readonly ElgatoCaptureService _capture = new();
    CancellationTokenSource? _cts;
    (int Width, int Height, double Fps) _actual;
    (int Width, int Height, int Fps) _requested = (1920, 1080, 60);

    public Ps5ElgatoWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Stop();
        RefreshDeviceList();
    }

    void RefreshDevices(object sender, RoutedEventArgs e) => RefreshDeviceList();

    void RefreshDeviceList()
    {
        var previous = (DeviceBox.SelectedItem as VideoDevice)?.Name;
        DeviceBox.ItemsSource = ElgatoCaptureService.EnumerateDevices();
        DeviceBox.SelectedItem = DeviceBox.Items.Cast<VideoDevice>().FirstOrDefault(x => x.Name == previous)
            ?? DeviceBox.Items.Cast<VideoDevice>().FirstOrDefault(x => x.Name.Contains("Elgato", StringComparison.OrdinalIgnoreCase))
            ?? DeviceBox.Items.Cast<VideoDevice>().FirstOrDefault();
        if (DeviceBox.Items.Count == 0) DiagnosticsText.Text = "No DirectShow video capture devices were found. Reconnect the 4K X, then select Refresh Devices.";
    }

    void StartCapture(object sender, RoutedEventArgs e)
    {
        if (DeviceBox.SelectedItem is not VideoDevice device)
        {
            MessageBox.Show(this, "Select the Elgato 4K X first.", "No capture device", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Stop();
            var parts = (((ComboBoxItem)FormatBox.SelectedItem).Tag?.ToString() ?? "1920,1080,60").Split(',').Select(int.Parse).ToArray();
            _requested = (parts[0], parts[1], parts[2]);
            _actual = _capture.Start(device.Index, _requested.Width, _requested.Height, _requested.Fps);
            _cts = new CancellationTokenSource();
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            DeviceBox.IsEnabled = FormatBox.IsEnabled = false;
            CapturePill.Text = "ELGATO CAPTURE ACTIVE";
            CapturePill.Foreground = System.Windows.Media.Brushes.LightGreen;
            _ = Task.Run(() => CaptureLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            Stop();
            MessageBox.Show(this, ex.Message, "Could not start capture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    async Task CaptureLoop(CancellationToken token)
    {
        var lastPreview = DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var sample = _capture.Read();
                if ((DateTime.UtcNow - lastPreview).TotalMilliseconds < 50) continue;
                lastPreview = DateTime.UtcNow;
                await Dispatcher.InvokeAsync(() =>
                {
                    PreviewImage.Source = sample.Preview;
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                    DiagnosticsText.Text = $"Requested  {_requested.Width} × {_requested.Height} @ {_requested.Fps}\nActual     {_actual.Width} × {_actual.Height} @ {_actual.Fps:F1}\nObserved   {sample.ObservedFps:F1} FPS\nAcquire    {sample.ReadMilliseconds:F1} ms\nFrame      {sample.FrameNumber}";
                });
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    DiagnosticsText.Text = "Capture stopped: " + ex.Message;
                    Stop();
                });
                break;
            }
        }
    }

    void StopCapture(object sender, RoutedEventArgs e) => Stop();

    void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _capture.Stop();
        if (!IsLoaded) return;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        DeviceBox.IsEnabled = FormatBox.IsEnabled = true;
        CapturePill.Text = "CAPTURE OFF";
        CapturePill.Foreground = System.Windows.Media.Brushes.LightGray;
    }

    void GuideToggle(object sender, RoutedEventArgs e)
    {
        if (GuideLayer is not null) GuideLayer.Visibility = PreviewGuides.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
}
