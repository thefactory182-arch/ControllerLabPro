using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using ControllerLabPro.Models;
using ControllerLabPro.Services;
using ControllerLabPro.Vision;
namespace ControllerLabPro;
public partial class MainWindow:Window
{
 readonly DualSenseReader _input=new();readonly VirtualXboxOutput _output=new();readonly WindowCapture _capture=new();readonly Stopwatch _clock=Stopwatch.StartNew();
 AppSettings _cfg=new();VisionAimEngine? _vision;IObjectDetector? _detector;DebugOverlay? _overlay;Ps5ElgatoWindow? _ps5Window;CancellationTokenSource? _visionCts;
 sealed record Correction(Stick Value);
 Correction _correction=new(new());
 Stick _visionStick{get=>Volatile.Read(ref _correction).Value;set=>Volatile.Write(ref _correction,new Correction(value));}
ControllerState? _last;
 readonly object _visionGate=new();
 readonly System.Windows.Threading.DispatcherTimer _monitorTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
 volatile bool _controllerRunning, _processingEnabled=true;
 bool _populating;
 AppSettings _runtimeCfg=new();
 ControllerState? _latestCooked;
 long _reportCount;
 string ProfileDir=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ControllerLabPro","Profiles");
 public MainWindow(){InitializeComponent();((DockPanel)SafetyPill.Parent).LastChildFill=false;Nav.Items.Add(new ListBoxItem{Content="PS5 / Elgato",Padding=new Thickness(12)});_input.StateChanged+=OnInput;_input.StatusChanged+=InputStatusChanged;
 _monitorTimer.Tick+=(_,_)=>RefreshMonitor();_monitorTimer.Start();
 DisplayBox.ItemsSource=WindowCapture.GetDisplays();DisplayBox.SelectedIndex=0;
 ConfigureAimChoices();ConfigureVisionReadouts();RefreshDashboardCopy();DebugOverlay.Content="Show visual indicators (display only)";DebugOverlay.ToolTip="Turn this off to hide the FOV circle, boxes, aim point and confidence. Vision Aim keeps working.";DebugOverlay.Checked+=OverlayToggle;DebugOverlay.Unchecked+=OverlayToggle;Loaded+=CheckPrerequisites;Closed+=(_,_)=>Shutdown();RefreshLabels();}
 void ConfigureAimChoices(){var labels=new[]{"Head","Body — Upper Torso","Body — Center Mass"};var values=new[]{AimPoint.Head,AimPoint.UpperTorso,AimPoint.CenterMass};for(var i=0;i<AimPointBox.Items.Count&&i<labels.Length;i++){if(AimPointBox.Items[i] is ComboBoxItem item){item.Content=labels[i];item.Tag=values[i].ToString();}}AimPointBox.ToolTip="Choose exactly where PC Vision Aim should help: head or body.";}
 void ConfigureVisionReadouts(){BindLabel(DetectionConfidence,v=>$"{v:P0}");BindLabel(FovRadius,v=>$"{v:P0} of screen height");BindLabel(AimStrength,v=>$"{v:P0}");BindLabel(Smoothing,v=>$"{v:P0}");BindLabel(MaxAimSpeed,v=>$"{v:P0} stick");BindLabel(LockMilliseconds,v=>$"{v:F0} ms");BindLabel(ReleaseMisses,v=>$"{v:F0} frames");}
 static void BindLabel(Slider slider,Func<double,string> format){if(slider.Parent is not Panel panel)return;var index=panel.Children.IndexOf(slider);if(index<1||panel.Children[index-1] is not TextBlock label)return;var title=label.Text;void Update()=>label.Text=$"{title}   •   {format(slider.Value)}";slider.ValueChanged+=(_,_)=>Update();slider.AutoToolTipPlacement=System.Windows.Controls.Primitives.AutoToolTipPlacement.BottomRight;slider.AutoToolTipPrecision=2;Update();}
 void RefreshDashboardCopy(){foreach(var text in Descendants<TextBlock>(DashboardPage))if(text.Text.StartsWith("Vision output requires:",StringComparison.Ordinal)){text.Text="Vision output requires the master switch, a selected display, L2/ADS activation, and a confident person detection inside the local FOV. Choose Head or either Body aim point on the Vision Aim page.";break;}}
 static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject{foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){if(child is T match)yield return match;foreach(var nested in Descendants<T>(child))yield return nested;}}
 async void CheckPrerequisites(object sender,RoutedEventArgs e)
 {
  Loaded-=CheckPrerequisites;
  if(PrerequisiteManager.IsViGEmInstalled())return;
  var answer=MessageBox.Show("ControllerLab Pro needs the ViGEmBus virtual-controller driver. It is not installed.\n\nDownload the official ViGEmBus 1.22.0 installer, verify it, and install it now? Windows will ask for administrator approval.","Required controller driver",MessageBoxButton.YesNo,MessageBoxImage.Information);
  if(answer!=MessageBoxResult.Yes){OutputStatus.Text="Xbox output unavailable — ViGEmBus is not installed";return;}
  try
  {
   var progress=new Progress<string>(text=>OutputStatus.Text=text);
   await PrerequisiteManager.InstallViGEmAsync(progress);
   OutputStatus.Text="ViGEmBus installed — ready to start controller";
   MessageBox.Show("ViGEmBus was installed successfully. You can now use virtual Xbox output.","Setup complete",MessageBoxButton.OK,MessageBoxImage.Information);
  }
  catch(Exception ex)
  {
   OutputStatus.Text="ViGEmBus setup did not complete";
   MessageBox.Show("ViGEmBus could not be installed. No downloaded installer was kept.\n\n"+ex.Message,"Setup failed",MessageBoxButton.OK,MessageBoxImage.Error);
  }
 }
 void InputStatusChanged(string status)
 {
  Dispatcher.BeginInvoke(()=>{
   if(status!=_input.Status)return;
   InputStatus.Text=status;
   if(_controllerRunning&&(status.Contains("input lost")||status.Contains("no reports")||status.Contains("processing failed")))
   {StopController();InputStatus.Text=status;MonitorText.Text=status;}
  });
 }
 void StartController(object s,RoutedEventArgs e)
 {
  if(_controllerRunning){StopController();return;}
  ApplyControls();
  if(!_output.Connect()){OutputStatus.Text=_output.Status;return;}
  _controllerRunning=true;
  if(!_input.Connect()){var failure=_input.Status;StopController();InputStatus.Text=failure;MonitorText.Text=failure;return;}
  ControllerButton.Content="STOP CONTROLLER";
  InputStatus.Text=_input.Status;OutputStatus.Text=_output.Status;
 }
 void StopController()
 {
  _controllerRunning=false;_input.Disconnect();_output.Disconnect();
  _last=null;_latestCooked=null;_reportCount=0;
  lock(_visionGate){_visionStick=new();_vision?.Release();}
  ControllerButton.Content="START CONTROLLER";InputStatus.Text=_input.Status;OutputStatus.Text=_output.Status;
  MonitorText.Text="Controller stopped";
 }
 void MasterToggle(object s,RoutedEventArgs e){_processingEnabled=MasterEnabled.IsChecked==true;}
 void OnInput(ControllerState raw)
 {
  if(!_controllerRunning)return;
  var cfg=Volatile.Read(ref _runtimeCfg);
  Volatile.Write(ref _last,raw);Interlocked.Increment(ref _reportCount);
  var right=ControllerPipeline.Tune(raw.Right,cfg.Sticks);
  right=ControllerPipeline.AddJitter(right,cfg.Jitter,_clock.Elapsed.TotalSeconds,raw.L2>.35);
  var active=!cfg.Vision.RequireActivation||raw.L2>.35;
  if(cfg.Vision.Enabled&&active&&_processingEnabled){var correction=_visionStick;right=new Stick(right.X+correction.X,right.Y+correction.Y).Clamp();}
  var buttons=new HashSet<string>();
  foreach(var button in raw.Buttons)buttons.Add(cfg.Remap.GetValueOrDefault(button,button));
  if(cfg.Turbo.Enabled&&buttons.Contains(cfg.Turbo.Button)&&((int)(_clock.Elapsed.TotalSeconds*cfg.Turbo.FrequencyHz*2)%2!=0))buttons.Remove(cfg.Turbo.Button);
  var cooked=_processingEnabled?new ControllerState(raw.Left,right,raw.L2,raw.R2,buttons):raw;
  _output.Submit(cooked);Volatile.Write(ref _latestCooked,cooked);
 }
 void RefreshMonitor()
 {
  var raw=Volatile.Read(ref _last);var cooked=Volatile.Read(ref _latestCooked);
  if(raw is null||cooked is null)return;
  MonitorText.Text=$"DualSense reports: {Interlocked.Read(ref _reportCount)}\nPhysical RX {raw.Right.X,7:F3}  RY {raw.Right.Y,7:F3}\nOutput   RX {cooked.Right.X,7:F3}  RY {cooked.Right.Y,7:F3}\nTriggers L2 {raw.L2,6:P0}  R2 {raw.R2,6:P0}\nButtons  {string.Join(", ",raw.Buttons)}";
 }
 async Task VisionLoop(CancellationToken ct)
 {
  while(!ct.IsCancellationRequested)
  {
   try
   {
    var cfg=Volatile.Read(ref _runtimeCfg).Vision;
    var captured=_capture.Capture(cfg.CaptureDisplay);
    if(captured is null)
    {
     lock(_visionGate){if(ct.IsCancellationRequested)return;_visionStick=new();_vision?.Release();}
     await Dispatcher.InvokeAsync(()=>{if(ct.IsCancellationRequested)return;VisionText.Text="Selected display unavailable. Select a connected display and Apply / Restart Vision.";_overlay?.Hide();});
     await Task.Delay(250,ct);continue;
    }
    using var frame=captured.Value.Image;
    var raw=Volatile.Read(ref _last);
    var active=_controllerRunning&&_processingEnabled&&raw is not null&&(!cfg.RequireActivation||raw.L2>.35);
    var sw=Stopwatch.StartNew();
    IReadOnlyList<Detection> detections;Detection? target;Stick correction;
    lock(_visionGate)
    {
     if(ct.IsCancellationRequested||_vision is null)return;
     if(active)_visionStick=_vision.Process(frame,cfg);else{_visionStick=new();_vision.Release();}
     detections=_vision.LastDetections;target=_vision.CurrentTarget;correction=_visionStick;
    }
    await Dispatcher.InvokeAsync(()=>{
     if(ct.IsCancellationRequested)return;
     VisionText.Text=$"Detector { (DetectorFactory.IsBuiltin(cfg.ModelPath)?"included YOLOX Nano":"custom YOLOv8") }\nDisplay {cfg.CaptureDisplay}\nCapture / inference {sw.ElapsedMilliseconds} ms\nActivation {(active?"HELD":!_controllerRunning?"start controller first":!_processingEnabled?"processing disabled":"hold L2 to activate")}\nAim point {cfg.AimPoint}\nCandidates {detections.Count}\nTarget {(target is null?"none":$"{target.Confidence:P0} confidence")}\nAim Δ {correction.X:F3}, {correction.Y:F3}";
     if(cfg.DebugOverlay&&active){_overlay??=new DebugOverlay();_overlay.Render(captured.Value.ScreenRect,detections,cfg.FovRadius,cfg.AimPoint);}else _overlay?.Hide();
    });
    await Task.Delay(10,ct);
   }
   catch(OperationCanceledException){break;}
   catch(Exception ex)
   {
    lock(_visionGate){if(ct.IsCancellationRequested)return;_visionStick=new();_vision?.Release();}
    await Dispatcher.InvokeAsync(()=>{if(!ct.IsCancellationRequested){VisionText.Text="Vision paused: "+ex.Message;_overlay?.Hide();}});
    try{await Task.Delay(500,ct);}catch(OperationCanceledException){break;}
   }
  }
 }
 void UseIncludedModel(object s,RoutedEventArgs e){ModelPath.Text=DetectorFactory.BuiltinModel;ApplyControls();VisionText.Text="Included detector selected. Enable Vision and click Apply / Restart Vision.";}
 void BrowseModel(object s,RoutedEventArgs e)
 {
  var dialog=new Microsoft.Win32.OpenFileDialog{Title="Select YOLOv8 person detector",Filter="ONNX model (*.onnx)|*.onnx",CheckFileExists=true};
  if(dialog.ShowDialog(this)==true){ModelPath.Text=dialog.FileName;ApplyControls();}
 }
 void RestartVision(object s,RoutedEventArgs e)
 {
  ApplyControls();StopVision();
  if(!_cfg.Vision.Enabled)return;
  if(DisplayBox.SelectedItem is not CaptureDisplay){VisionText.Text="Select a connected display first.";VisionEnabled.IsChecked=false;return;}
  if(!DetectorFactory.IsBuiltin(_cfg.Vision.ModelPath)&&!File.Exists(DetectorFactory.ResolvePath(_cfg.Vision.ModelPath))){VisionText.Text="Custom model not found. Click USE INCLUDED DETECTOR, or browse to an existing YOLOv8 ONNX file. Path: "+DetectorFactory.ResolvePath(_cfg.Vision.ModelPath);VisionEnabled.IsChecked=false;return;}
  try
  {
   lock(_visionGate){_detector=DetectorFactory.Create(_cfg.Vision.ModelPath);_vision=new(_detector,new ProportionalTargetPointEstimator());}
   _visionCts=new();var token=_visionCts.Token;_=Task.Run(()=>VisionLoop(token));
   SafetyPill.Child=new TextBlock{Text="VISION AIM / HOLD L2",Foreground=System.Windows.Media.Brushes.Gold};
  }
  catch(Exception ex){VisionText.Text="Could not start model: "+ex.Message;VisionEnabled.IsChecked=false;}
 }
 void StopVision()
 {
  _visionCts?.Cancel();_visionCts=null;
  lock(_visionGate){_detector?.Dispose();_detector=null;_vision=null;_visionStick=new();}
  _overlay?.Hide();SafetyPill.Child=new TextBlock{Text="VISION SAFE / OFF",Foreground=System.Windows.Media.Brushes.LightGray};
 }
 void DisableVision(object s,RoutedEventArgs e){VisionEnabled.IsChecked=false;_cfg.Vision.Enabled=false;StopVision();}
 void VisionToggle(object s,RoutedEventArgs e){if(!IsLoaded||_populating)return;ApplyControls();if(!_cfg.Vision.Enabled)StopVision();else VisionText.Text="Select your display and model, then click Apply / Restart Vision.";}
 void OverlayToggle(object s,RoutedEventArgs e){if(!IsLoaded||_populating)return;ApplyControls();if(!_cfg.Vision.DebugOverlay)_overlay?.Hide();}
 void ApplyControls(){_cfg.ProfileName=ProfileName.Text;_cfg.Sticks.Sensitivity=Sensitivity.Value;_cfg.Jitter.Enabled=JitterEnabled.IsChecked==true;_cfg.Jitter.Pattern=Enum.Parse<JitterPattern>(((ComboBoxItem)JitterPatternBox.SelectedItem).Content.ToString()!);_cfg.Jitter.Horizontal=JitterX.Value;_cfg.Jitter.Vertical=JitterY.Value;_cfg.Jitter.FrequencyHz=JitterHz.Value;_cfg.Turbo.Enabled=TurboEnabled.IsChecked==true;_cfg.Turbo.Button=((ComboBoxItem)TurboButton.SelectedItem).Content.ToString()!;_cfg.Turbo.FrequencyHz=TurboHz.Value;_cfg.Vision.Enabled=VisionEnabled.IsChecked==true;_cfg.Vision.CaptureDisplay=(DisplayBox.SelectedItem as CaptureDisplay)?.DeviceName??"";_cfg.Vision.ModelPath=ModelPath.Text;var aimItem=(ComboBoxItem)AimPointBox.SelectedItem;_cfg.Vision.AimPoint=Enum.Parse<AimPoint>(aimItem.Tag?.ToString()??aimItem.Content.ToString()!);_cfg.Vision.DebugOverlay=DebugOverlay.IsChecked==true;_cfg.Vision.DetectionConfidence=DetectionConfidence.Value;_cfg.Vision.FovRadius=FovRadius.Value;_cfg.Vision.AimStrength=AimStrength.Value;_cfg.Vision.Smoothing=Smoothing.Value;_cfg.Vision.MaxAimSpeed=MaxAimSpeed.Value;_cfg.Vision.LockMilliseconds=(int)LockMilliseconds.Value;_cfg.Vision.ReleaseAfterMissedFrames=(int)ReleaseMisses.Value;Volatile.Write(ref _runtimeCfg,System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(_cfg))!);}
 void Populate(){_populating=true;try{ProfileName.Text=_cfg.ProfileName;Sensitivity.Value=_cfg.Sticks.Sensitivity;JitterEnabled.IsChecked=_cfg.Jitter.Enabled;Select(JitterPatternBox,_cfg.Jitter.Pattern.ToString());JitterX.Value=_cfg.Jitter.Horizontal;JitterY.Value=_cfg.Jitter.Vertical;JitterHz.Value=_cfg.Jitter.FrequencyHz;TurboEnabled.IsChecked=_cfg.Turbo.Enabled;Select(TurboButton,_cfg.Turbo.Button);TurboHz.Value=_cfg.Turbo.FrequencyHz;VisionEnabled.IsChecked=_cfg.Vision.Enabled;DisplayBox.SelectedItem=WindowCapture.GetDisplays().FirstOrDefault(d=>d.DeviceName==_cfg.Vision.CaptureDisplay)??DisplayBox.Items.Cast<CaptureDisplay>().FirstOrDefault();ModelPath.Text=_cfg.Vision.ModelPath;Select(AimPointBox,_cfg.Vision.AimPoint.ToString());DebugOverlay.IsChecked=_cfg.Vision.DebugOverlay;DetectionConfidence.Value=_cfg.Vision.DetectionConfidence;FovRadius.Value=_cfg.Vision.FovRadius;AimStrength.Value=_cfg.Vision.AimStrength;Smoothing.Value=_cfg.Vision.Smoothing;MaxAimSpeed.Value=_cfg.Vision.MaxAimSpeed;LockMilliseconds.Value=_cfg.Vision.LockMilliseconds;ReleaseMisses.Value=_cfg.Vision.ReleaseAfterMissedFrames;RefreshLabels();}finally{_populating=false;}}
 static void Select(ComboBox box,string value){foreach(ComboBoxItem i in box.Items)i.IsSelected=string.Equals(i.Tag?.ToString()??i.Content?.ToString(),value,StringComparison.OrdinalIgnoreCase);}
 void SaveProfile(object s,RoutedEventArgs e){ApplyControls();_cfg.Save(Path.Combine(ProfileDir,SafeName(_cfg.ProfileName)+".json"));}
 void LoadProfile(object s,RoutedEventArgs e){var p=Path.Combine(ProfileDir,SafeName(ProfileName.Text)+".json");StopVision();_cfg=AppSettings.Load(p);Populate();ApplyControls();if(_cfg.Vision.Enabled)RestartVision(s,e);}
 static string SafeName(string s)=>string.Concat(s.Where(c=>char.IsLetterOrDigit(c)||c is '-' or '_')) is var v&&v.Length>0?v:"Default";
 void SettingsChanged(object s,RoutedEventArgs e){if(!IsLoaded||_populating)return;ApplyControls();RefreshLabels();}
 void RefreshLabels(){if(SensitivityValue is null)return;SensitivityValue.Text=$"{Sensitivity.Value:F2}×";JitterXValue.Text=$"{JitterX.Value:P1}";JitterYValue.Text=$"{JitterY.Value:P1}";JitterHzValue.Text=$"{JitterHz.Value:F0} Hz";}
 void NavChanged(object s,SelectionChangedEventArgs e){if(!IsLoaded||Nav.SelectedItem is null)return;if(Nav.SelectedIndex==6){_ps5Window??=new Ps5ElgatoWindow{Owner=this};_ps5Window.Closed+=(_,_)=>_ps5Window=null;_ps5Window.Show();_ps5Window.Activate();Nav.SelectedIndex=0;return;}var pages=new[]{DashboardPage,SticksPage,TurboPage,VisionPage,MonitorPage,ProfilesPage};for(int i=0;i<pages.Length;i++)pages[i].Visibility=i==Nav.SelectedIndex?Visibility.Visible:Visibility.Collapsed;PageTitle.Text=((ListBoxItem)Nav.SelectedItem).Content.ToString();}
 void Shutdown(){_monitorTimer.Stop();StopVision();StopController();_input.Dispose();_output.Dispose();_overlay?.Close();_ps5Window?.Close();}
}
