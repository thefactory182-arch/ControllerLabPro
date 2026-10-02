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
 AppSettings _cfg=new();VisionAimEngine? _vision;IObjectDetector? _detector;DebugOverlay? _overlay;Ps5ElgatoWindow? _ps5Window;CancellationTokenSource? _visionCts;Stick _visionStick;ControllerState? _last;bool _turboPhase;
 string ProfileDir=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ControllerLabPro","Profiles");
 public MainWindow(){InitializeComponent();((DockPanel)SafetyPill.Parent).LastChildFill=false;Nav.Items.Add(new ListBoxItem{Content="PS5 / Elgato",Padding=new Thickness(12)});ConfigureAimChoices();ConfigureVisionReadouts();RefreshDashboardCopy();DebugOverlay.Content="Show visual indicators (display only)";DebugOverlay.ToolTip="Turn this off to hide the FOV circle, boxes, aim point and confidence. Vision Aim keeps working.";DebugOverlay.Checked+=OverlayToggle;DebugOverlay.Unchecked+=OverlayToggle;Loaded+=CheckPrerequisites;Closed+=(_,_)=>Shutdown();RefreshLabels();}
 void ConfigureAimChoices(){var labels=new[]{"Head","Body — Upper Torso","Body — Center Mass"};var values=new[]{AimPoint.Head,AimPoint.UpperTorso,AimPoint.CenterMass};for(var i=0;i<AimPointBox.Items.Count&&i<labels.Length;i++){if(AimPointBox.Items[i] is ComboBoxItem item){item.Content=labels[i];item.Tag=values[i].ToString();}}AimPointBox.ToolTip="Choose exactly where PC Vision Aim should help: head or body.";}
 void ConfigureVisionReadouts(){BindLabel(DetectionConfidence,v=>$"{v:P0}");BindLabel(FovRadius,v=>$"{v:P0} of screen height");BindLabel(AimStrength,v=>$"{v:P0}");BindLabel(Smoothing,v=>$"{v:P0}");BindLabel(MaxAimSpeed,v=>$"{v:P0} stick");BindLabel(LockMilliseconds,v=>$"{v:F0} ms");BindLabel(ReleaseMisses,v=>$"{v:F0} frames");}
 static void BindLabel(Slider slider,Func<double,string> format){if(slider.Parent is not Panel panel)return;var index=panel.Children.IndexOf(slider);if(index<1||panel.Children[index-1] is not TextBlock label)return;var title=label.Text;void Update()=>label.Text=$"{title}   •   {format(slider.Value)}";slider.ValueChanged+=(_,_)=>Update();slider.AutoToolTipPlacement=System.Windows.Controls.Primitives.AutoToolTipPlacement.BottomRight;slider.AutoToolTipPrecision=2;Update();}
 void RefreshDashboardCopy(){foreach(var text in Descendants<TextBlock>(DashboardPage))if(text.Text.StartsWith("Vision output requires:",StringComparison.Ordinal)){text.Text="Vision output requires the master switch, a detected game window, L2/ADS activation, and a confident person detection inside the local FOV. Choose Head or either Body aim point on the Vision Aim page.";break;}}
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
 void StartController(object s,RoutedEventArgs e){if(_input.Connect()){_input.StateChanged+=OnInput;}_output.Connect();InputStatus.Text=_input.Status;OutputStatus.Text=_output.Status;}
 void OnInput(ControllerState raw)
 {
  _last=raw;var right=ControllerPipeline.Tune(raw.Right,_cfg.Sticks);right=ControllerPipeline.AddJitter(right,_cfg.Jitter,_clock.Elapsed.TotalSeconds);
  var active=!_cfg.Vision.RequireActivation||raw.L2>.35;if(_cfg.Vision.Enabled&&active)right=new Stick(right.X+_visionStick.X,right.Y+_visionStick.Y).Clamp();else if(!active){_visionStick=new();_vision?.Release();}
  var buttons=new HashSet<string>(raw.Buttons);foreach(var m in _cfg.Remap)if(buttons.Remove(m.Key))buttons.Add(m.Value);if(_cfg.Turbo.Enabled&&buttons.Contains(_cfg.Turbo.Button)){_turboPhase=(int)(_clock.Elapsed.TotalSeconds*_cfg.Turbo.FrequencyHz*2)%2==0;if(!_turboPhase)buttons.Remove(_cfg.Turbo.Button);}
  var cooked=new ControllerState(raw.Left,right,raw.L2,raw.R2,buttons);if(MasterEnabled.IsChecked==true)_output.Submit(cooked);
  Dispatcher.BeginInvoke(()=>MonitorText.Text=$"Physical RX {raw.Right.X,7:F3}  RY {raw.Right.Y,7:F3}\nOutput   RX {right.X,7:F3}  RY {right.Y,7:F3}\nTriggers L2 {raw.L2,6:P0}  R2 {raw.R2,6:P0}\nButtons  {string.Join(", ",raw.Buttons)}");
 }
 async Task VisionLoop(CancellationToken ct)
 {
  while(!ct.IsCancellationRequested&&_vision is not null){try{var c=_capture.Capture(_cfg.Vision.WindowTitleContains);if(c is null){_visionStick=new();await Dispatcher.BeginInvoke(()=>VisionText.Text="Game window not found. Check the exact title fragment.");await Task.Delay(250,ct);continue;}using var frame=c.Value.Image;var active=_last is not null&&(!_cfg.Vision.RequireActivation||_last.L2>.35);var sw=Stopwatch.StartNew();if(active)_visionStick=_vision.Process(frame,_cfg.Vision);else{_visionStick=new();_vision.Release();}var ds=_vision.LastDetections;var target=_vision.CurrentTarget;await Dispatcher.BeginInvoke(()=>{VisionText.Text=$"Frame {sw.ElapsedMilliseconds} ms\nActivation {(active?"HELD":"released")}\nAim point {_cfg.Vision.AimPoint}\nCandidates {ds.Count}\nTarget {(target is null?"none":$"{target.Confidence:P0} confidence")}\nAim Δ {_visionStick.X:F3}, {_visionStick.Y:F3}";if(_cfg.Vision.DebugOverlay){_overlay??=new DebugOverlay();_overlay.Render(c.Value.ScreenRect,ds,_cfg.Vision.FovRadius,_cfg.Vision.AimPoint);}else _overlay?.Hide();});await Task.Delay(1,ct);}catch(OperationCanceledException){}catch(Exception ex){_visionStick=new();await Dispatcher.BeginInvoke(()=>VisionText.Text="Vision paused: "+ex.Message);await Task.Delay(500,ct);}}
 }
 void RestartVision(object s,RoutedEventArgs e){ApplyControls();StopVision();if(!_cfg.Vision.Enabled)return;if(string.IsNullOrWhiteSpace(_cfg.Vision.WindowTitleContains)){VisionText.Text="Enter a game window title fragment first.";VisionEnabled.IsChecked=false;return;}if(!File.Exists(_cfg.Vision.ModelPath)){VisionText.Text="Model not found: "+Path.GetFullPath(_cfg.Vision.ModelPath);VisionEnabled.IsChecked=false;return;}try{_detector=new YoloOnnxDetector(_cfg.Vision.ModelPath);_vision=new(_detector,new ProportionalTargetPointEstimator());_visionCts=new();_=Task.Run(()=>VisionLoop(_visionCts.Token));SafetyPill.Child=new TextBlock{Text="VISION AIM / HOLD L2",Foreground=System.Windows.Media.Brushes.Gold};}catch(Exception ex){VisionText.Text="Could not start model: "+ex.Message;VisionEnabled.IsChecked=false;}}
 void StopVision(){_visionCts?.Cancel();_visionCts=null;_detector?.Dispose();_detector=null;_vision=null;_visionStick=new();_overlay?.Hide();SafetyPill.Child=new TextBlock{Text="VISION SAFE / OFF",Foreground=System.Windows.Media.Brushes.LightGray};}
 void DisableVision(object s,RoutedEventArgs e){VisionEnabled.IsChecked=false;_cfg.Vision.Enabled=false;StopVision();}
 void VisionToggle(object s,RoutedEventArgs e){if(!IsLoaded)return;_cfg.Vision.Enabled=VisionEnabled.IsChecked==true;if(!_cfg.Vision.Enabled)StopVision();}
 void OverlayToggle(object s,RoutedEventArgs e){if(!IsLoaded)return;_cfg.Vision.DebugOverlay=DebugOverlay.IsChecked==true;if(!_cfg.Vision.DebugOverlay)_overlay?.Hide();}
 void ApplyControls(){_cfg.ProfileName=ProfileName.Text;_cfg.Sticks.RightDeadzone=Deadzone.Value;_cfg.Sticks.Sensitivity=Sensitivity.Value;_cfg.Sticks.AntiDeadzone=AntiDeadzone.Value;_cfg.Jitter.Enabled=JitterEnabled.IsChecked==true;_cfg.Jitter.Pattern=Enum.Parse<JitterPattern>(((ComboBoxItem)JitterPatternBox.SelectedItem).Content.ToString()!);_cfg.Jitter.Horizontal=JitterX.Value;_cfg.Jitter.Vertical=JitterY.Value;_cfg.Jitter.FrequencyHz=JitterHz.Value;_cfg.Turbo.Enabled=TurboEnabled.IsChecked==true;_cfg.Turbo.Button=((ComboBoxItem)TurboButton.SelectedItem).Content.ToString()!;_cfg.Turbo.FrequencyHz=TurboHz.Value;_cfg.Vision.Enabled=VisionEnabled.IsChecked==true;_cfg.Vision.WindowTitleContains=WindowTitle.Text;_cfg.Vision.ModelPath=ModelPath.Text;var aimItem=(ComboBoxItem)AimPointBox.SelectedItem;_cfg.Vision.AimPoint=Enum.Parse<AimPoint>(aimItem.Tag?.ToString()??aimItem.Content.ToString()!);_cfg.Vision.DebugOverlay=DebugOverlay.IsChecked==true;_cfg.Vision.DetectionConfidence=DetectionConfidence.Value;_cfg.Vision.FovRadius=FovRadius.Value;_cfg.Vision.AimStrength=AimStrength.Value;_cfg.Vision.Smoothing=Smoothing.Value;_cfg.Vision.MaxAimSpeed=MaxAimSpeed.Value;_cfg.Vision.LockMilliseconds=(int)LockMilliseconds.Value;_cfg.Vision.ReleaseAfterMissedFrames=(int)ReleaseMisses.Value;}
 void Populate(){ProfileName.Text=_cfg.ProfileName;Deadzone.Value=_cfg.Sticks.RightDeadzone;Sensitivity.Value=_cfg.Sticks.Sensitivity;AntiDeadzone.Value=_cfg.Sticks.AntiDeadzone;JitterEnabled.IsChecked=_cfg.Jitter.Enabled;Select(JitterPatternBox,_cfg.Jitter.Pattern.ToString());JitterX.Value=_cfg.Jitter.Horizontal;JitterY.Value=_cfg.Jitter.Vertical;JitterHz.Value=_cfg.Jitter.FrequencyHz;TurboEnabled.IsChecked=_cfg.Turbo.Enabled;Select(TurboButton,_cfg.Turbo.Button);TurboHz.Value=_cfg.Turbo.FrequencyHz;VisionEnabled.IsChecked=_cfg.Vision.Enabled;WindowTitle.Text=_cfg.Vision.WindowTitleContains;ModelPath.Text=_cfg.Vision.ModelPath;Select(AimPointBox,_cfg.Vision.AimPoint.ToString());DebugOverlay.IsChecked=_cfg.Vision.DebugOverlay;DetectionConfidence.Value=_cfg.Vision.DetectionConfidence;FovRadius.Value=_cfg.Vision.FovRadius;AimStrength.Value=_cfg.Vision.AimStrength;Smoothing.Value=_cfg.Vision.Smoothing;MaxAimSpeed.Value=_cfg.Vision.MaxAimSpeed;LockMilliseconds.Value=_cfg.Vision.LockMilliseconds;ReleaseMisses.Value=_cfg.Vision.ReleaseAfterMissedFrames;RefreshLabels();}
 static void Select(ComboBox box,string value){foreach(ComboBoxItem i in box.Items)i.IsSelected=string.Equals(i.Tag?.ToString()??i.Content?.ToString(),value,StringComparison.OrdinalIgnoreCase);}
 void SaveProfile(object s,RoutedEventArgs e){ApplyControls();_cfg.Save(Path.Combine(ProfileDir,SafeName(_cfg.ProfileName)+".json"));}
 void LoadProfile(object s,RoutedEventArgs e){var p=Path.Combine(ProfileDir,SafeName(ProfileName.Text)+".json");_cfg=AppSettings.Load(p);Populate();}
 static string SafeName(string s)=>string.Concat(s.Where(c=>char.IsLetterOrDigit(c)||c is '-' or '_')) is var v&&v.Length>0?v:"Default";
 void SettingsChanged(object s,RoutedEventArgs e){if(!IsLoaded)return;ApplyControls();RefreshLabels();}
 void RefreshLabels(){if(DeadzoneValue is null)return;DeadzoneValue.Text=$"{Deadzone.Value:P0}";SensitivityValue.Text=$"{Sensitivity.Value:F2}×";AntiDeadzoneValue.Text=$"{AntiDeadzone.Value:P0}";JitterXValue.Text=$"{JitterX.Value:P0}";JitterYValue.Text=$"{JitterY.Value:P0}";JitterHzValue.Text=$"{JitterHz.Value:F0} Hz";}
 void NavChanged(object s,SelectionChangedEventArgs e){if(!IsLoaded||Nav.SelectedItem is null)return;if(Nav.SelectedIndex==6){_ps5Window??=new Ps5ElgatoWindow{Owner=this};_ps5Window.Closed+=(_,_)=>_ps5Window=null;_ps5Window.Show();_ps5Window.Activate();Nav.SelectedIndex=0;return;}var pages=new[]{DashboardPage,SticksPage,TurboPage,VisionPage,MonitorPage,ProfilesPage};for(int i=0;i<pages.Length;i++)pages[i].Visibility=i==Nav.SelectedIndex?Visibility.Visible:Visibility.Collapsed;PageTitle.Text=((ListBoxItem)Nav.SelectedItem).Content.ToString();}
 void Shutdown(){StopVision();_input.Dispose();_output.Dispose();_overlay?.Close();_ps5Window?.Close();}
}
