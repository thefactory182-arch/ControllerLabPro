# ControllerLab Pro

ControllerLab Pro is a Windows controller-processing app intended **only for offline and story-mode games**. It reads a DualSense, applies sensitivity, L2-only low-strength jitter, turbo and button remaps, and sends the result to a virtual Xbox 360 controller. Its optional Vision Aim page applies a bounded right-stick correction only while L2 is held.

## Vision Aim behavior

The vision loop captures the selected display (including visible fullscreen borderless games), crops a configurable region around the screen crosshair, runs the configured YOLO-style ONNX person detector on that local crop, and considers only aim points inside the FOV circle. It selects the person aim point nearest the crosshair and smoothly corrects toward Head, Upper Torso, or Center Mass.

Locking is matched geometrically between frames. A target releases when activation is released, it leaves the FOV, the lock duration expires, the configured number of frames are missed, the master switch is turned off, or capture/model processing fails. Corrections are capped by the maximum stick speed setting.

The optional debug overlay shows the FOV circle, every detected person box, the inferred aim point, confidence, and the current target. Turn off **Show visual indicators (display only)** at any time to hide all on-screen indicators without disabling detection, target locking, or stick correction.

On the PC **Vision Aim** page, the aim-point selector explicitly offers **Head**, **Body — Upper Torso**, and **Body — Center Mass**. This selection is saved with the profile and appears in live diagnostics and the overlay.

## Important limitation

**A generic person/head detector cannot distinguish friendly characters from enemies.** Crosshair-local activation reduces accidental teammate targeting because the app only assists near where you are already aiming, but it does not eliminate that risk. This project does not claim enemy-only recognition.

No anti-cheat bypass, process injection, concealment, game-memory reading, or online-match support is included. Do not use this in multiplayer or competitive play.

## Build and setup

Requirements:

1. Windows 10/11 x64. The .NET 10 SDK is needed only to build from source; the released executable is self-contained.
2. ViGEmBus installed for virtual Xbox output. On first launch, ControllerLab Pro checks for it and can download the official 1.22.0 installer, validate its SHA-256 checksum, and install it after Windows administrator approval.
3. A USB or Bluetooth DualSense controller.
4. No separate model is required for the released executable: YOLOX Nano is embedded. Custom YOLOv8 ONNX models remain optional. Building from source runs `setup-model.ps1` to download and verify the official model before embedding it.

Run `build.ps1` from PowerShell. It restores packages, builds Release, and publishes a self-contained Windows x64 app to `artifacts/publish`. Use `build.ps1 -SkipPublish` for a quicker build verification.

```powershell
.\setup-model.ps1
dotnet restore .\ControllerLabPro.slnx
dotnet build .\ControllerLabPro.slnx -c Release
dotnet publish .\src\ControllerLabPro\ControllerLabPro.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\publish
```

## Pluggable detector and localization

`IObjectDetector` is the detector boundary; `YoloXOnnxDetector` is the included default; `YoloOnnxDetector` supports optional custom YOLOv8 exports. `ITargetPointEstimator` is the localization boundary. The included `ProportionalTargetPointEstimator` estimates head, upper-torso, and center-mass points from the person box. A lightweight pose/keypoint implementation can replace it without changing capture, FOV selection, lock, smoothing, or controller output logic.

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

The checks cover all three aim points, nearest-target selection, maximum correction speed, lost-target release, sensitivity/clamping, construction of both WPF windows, dark-theme resources, and the visible Head/Body choices. Physical DualSense, ViGEmBus, Elgato, and model-inference timing still require the respective hardware and ONNX model on the target PC.

## Version 1.0.1 fixes

- Controller input processing runs without accessing WPF controls from the HID thread; the live monitor refreshes at 30 Hz without slowing controller output.
- Start toggles to Stop, and Stop/disconnect clears input and releases the virtual controller. Start failures and lost-input errors include useful diagnostics.
- USB, full Bluetooth, and basic Bluetooth DualSense reports are parsed separately; short read timeouts are retried. D-pad, shoulders, menu, stick-click and Guide buttons now reach the virtual output.
- The physical input is explicitly labeled DualSense. The game output is a virtual Xbox 360 controller by design: select that controller in your game to use tuning, jitter, turbo, remaps and vision correction. If your game directly uses the physical DualSense, it bypasses the processed output. Close competing controller translators or configure your existing HidHide setup to allow ControllerLab Pro access.
- Turbo controls apply immediately. Turning processing off passes raw input through rather than leaving stale held buttons in the game.
- Vision captures a chosen monitor instead of requiring a window title. Select the display your game is visible on, enable Vision, browse to a YOLOv8 ONNX person model, then Apply / Restart Vision. Hold L2 with the controller running. No model is bundled; diagnostics explain missing models, capture failures, activation, candidate counts and correction values.
- Main-window background and selected navigation rows use explicit dark colors. Regression screenshots now use the actual window background.

Validation: Release build with zero warnings/errors; 10/10 regression checks (including USB/Bluetooth fixtures, background-thread processing, monitor refresh, bypass/stop, display persistence and actual WPF backgrounds). Physical controller/ViGEmBus, in-game output, real-model inference and Elgato still need hardware validation.

## Version 1.0.2

Deadzone and anti-deadzone controls and processing have been removed, including from legacy profiles; configure deadzones inside your game. Sensitivity remains a simple gain.

Jitter is a small right-stick oscillation only while L2 exceeds 35%. Release L2 to stop immediately. Default horizontal/vertical strength is 0.5%; maximum is 3% (legacy values are clamped too). The old 30% maximum caused pronounced camera shaking. Even small oscillations can remain visible at high game sensitivity; this is not a guarantee of aim improvement.

Vision now includes the official Apache-2.0 YOLOX Nano detector inside the executable. Select USE INCLUDED DETECTOR, enable Vision, select your game display, then APPLY / RESTART VISION and hold L2. The previous missing `models/yolov8n.onnx` default automatically falls back to the included detector. Custom YOLOv8 models are optional, with relative custom paths resolved against the executable directory. Profiles load without control events overwriting their saved settings.

Validation: 12/12 checks, including jitter activation/release and legacy limits, deadzone removal, embedded-model loading, and genuine inference on a sample containing people. Physical game behavior still requires target-PC testing. Included model origin, pinned SHA-256 and license are documented in `src/ControllerLabPro/Models/README.md` and `YOLOX-LICENSE.txt`.

## Version 1.0.3

Vision has two primary movement controls: Strength and Smoothness. Confidence and detection radius are collapsed under Advanced. Strength sets correction gain and speed cap; smoothness sets a time-based response (20-300 ms time constant), so it behaves consistently across inference frame rates. It eases toward a visible aim point while L2 is held; this does not guarantee a snap or a human-looking motion in every game.

Virtual Xbox output is submitted every 8 ms independently of physical HID reports and model processing. The panel shows the actual processed right-stick values and output report count. Releasing L2 immediately gates vision off; stale corrections expire after 250 ms and stale physical input produces a neutral report after 500 ms. No app deadzone processing has been added. Your game's right-stick deadzone can ignore small corrections. Select the app's virtual controller; Xbox prompts alone do not conclusively identify which controller source a game uses.

YOLOX-S is included as the stronger default ONNX detector. YOLOX Nano remains a faster included option, and compatible custom YOLOv8 ONNX files remain supported. Both included models are official exports, hash verified and embedded. The model selector must be followed by Apply / Restart Vision to load a different model.

Jitter is experimental oscillation with no verified Call of Duty aim-assist improvement. It is suppressed whenever Vision is enabled to avoid competing movement. The app only sees visible screen pixels: it cannot draw accurate boxes around people hidden behind walls or distinguish enemy/friendly allegiance. A red box indicates target selection, not confirmed game movement.

Validation: 14/14 checks, including genuine inference on both included model families, FPS-independent smoothing, correction-to-controller pipeline, repeat output without new HID reports, immediate L2 release, stale-input/correction cleanup and a closed-loop aiming simulation. Actual Call of Duty motion has not been verified on target hardware.
