# ControllerLab Pro

ControllerLab Pro is a Windows controller-processing app intended **only for offline and story-mode games**. It reads a DualSense, applies stick tuning, jitter, turbo and button remaps, and sends the result to a virtual Xbox 360 controller. Its optional Vision Aim page applies a bounded right-stick correction only while L2 is held.

## Vision Aim behavior

The vision loop captures the selected game window, crops a configurable region around the screen crosshair, runs the configured YOLO-style ONNX person detector on that local crop, and considers only aim points inside the FOV circle. It selects the person aim point nearest the crosshair and smoothly corrects toward Head, Upper Torso, or Center Mass.

Locking is matched geometrically between frames. A target releases when activation is released, it leaves the FOV, the lock duration expires, the configured number of frames are missed, the master switch is turned off, or capture/model processing fails. Corrections are capped by the maximum stick speed setting.

The optional debug overlay shows the FOV circle, every detected person box, the inferred aim point, confidence, and the current target. Turn off **Show visual indicators (display only)** at any time to hide all on-screen indicators without disabling detection, target locking, or stick correction.

On the PC **Vision Aim** page, the aim-point selector explicitly offers **Head**, **Body — Upper Torso**, and **Body — Center Mass**. This selection is saved with the profile and appears in live diagnostics and the overlay.

## Important limitation

**A generic person/head detector cannot distinguish friendly characters from enemies.** Crosshair-local activation reduces accidental teammate targeting because the app only assists near where you are already aiming, but it does not eliminate that risk. This project does not claim enemy-only recognition.

No anti-cheat bypass, process injection, concealment, game-memory reading, or online-match support is included. Do not use this in multiplayer or competitive play.

## Build and setup

Requirements:

1. Windows 10/11 x64 and the .NET 10 SDK.
2. ViGEmBus installed for virtual Xbox output. On first launch, ControllerLab Pro checks for it and can download the official 1.22.0 installer, validate its SHA-256 checksum, and install it after Windows administrator approval.
3. A USB or Bluetooth DualSense controller.
4. A YOLOv8-style ONNX person model. Put `yolov8n.onnx` at `src/ControllerLabPro/models/yolov8n.onnx` or choose its path on Vision Aim.

Run `build.ps1` from PowerShell. It restores packages, builds Release, and publishes a self-contained Windows x64 app to `artifacts/publish`. Use `build.ps1 -SkipPublish` for a quicker build verification.

```powershell
dotnet restore .\ControllerLabPro.slnx
dotnet build .\ControllerLabPro.slnx -c Release
dotnet publish .\src\ControllerLabPro\ControllerLabPro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\publish
```

## Pluggable detector and localization

`IObjectDetector` is the detector boundary; `YoloOnnxDetector` is the default ONNX implementation. `ITargetPointEstimator` is the localization boundary. The included `ProportionalTargetPointEstimator` estimates head, upper-torso, and center-mass points from the person box. A lightweight pose/keypoint implementation can replace it without changing capture, FOV selection, lock, smoothing, or controller output logic.

Profiles are JSON files under `%LOCALAPPDATA%\ControllerLabPro\Profiles`. Frames are processed locally and are not saved.

## PS5 / Elgato workspace

The PS5 features are intentionally separate from the existing PC pipeline. Select **PS5 / Elgato** in the sidebar to open an independent console-capture window. It enumerates DirectShow devices, prefers an Elgato device automatically, and offers 1080p60, 720p60, and 4K30 capture. The recommended starting format is 1080p60 SDR while playing through the 4K X HDMI passthrough.

The workspace provides a preview, optional preview-only crosshair/FOV guides, requested and negotiated formats, observed capture FPS, acquisition time, and frame count. Another application such as OBS or Elgato 4K Capture Utility may need to release the device before ControllerLab Pro can open it.

PS5 controller output is deliberately disabled until a documented and supported Windows-to-PS5 return path is configured. Zen LINK can connect a Cronus Zen to PS5, but it does not by itself expose a documented real-time input API for this Windows application. The existing PC virtual Xbox output and PC Vision Aim configuration remain unchanged.

## Performance and verification

The ONNX input conversion uses locked bitmap memory rather than per-pixel `GetPixel` calls, ONNX Runtime graph optimizations are enabled, DirectML is preferred when available, the PC vision loop no longer carries an intentional 25 ms delay, and Elgato capture requests a one-frame buffer.

Run the repeatable checks with:

```powershell
dotnet run --project .\tests\ControllerLabPro.SmokeTests\ControllerLabPro.SmokeTests.csproj -c Release
```

The checks cover all three aim points, nearest-target selection, maximum correction speed, lost-target release, stick deadzone/clamping, construction of both WPF windows, dark-theme resources, and the visible Head/Body choices. Physical DualSense, ViGEmBus, Elgato, and model-inference timing still require the respective hardware and ONNX model on the target PC.
