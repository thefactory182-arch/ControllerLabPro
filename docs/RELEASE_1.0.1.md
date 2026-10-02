ControllerLab Pro 1.0.1 fixes the controller input and UI failures in 1.0.0.

- Fixed background-thread WPF access that stopped DualSense input on the first report.
- Start / Stop toggle with cleanup on stop, disconnect and failed startup.
- USB, Bluetooth full/basic reports, timeout retries and complete gamepad button forwarding.
- Clear physical DualSense and virtual Xbox game-output labels.
- Immediate turbo settings, raw-input bypass when processing is off and thread-safe vision correction.
- Display selection for visible fullscreen borderless games, model file browser and clearer vision diagnostics.
- Explicit dark main-window background and readable selected navigation rows.

Download **ControllerLabPro.exe**. Select the app's **virtual Xbox 360 controller** in your game for processed input. Your physical controller is still a DualSense.

Vision requires a YOLOv8 ONNX person model (not bundled). Select your game display, enable Vision, choose the model, Apply / Restart Vision, and hold L2 with the controller running.

Validation: Release build with zero warnings/errors and 10/10 regression checks. USB/Bluetooth tests use recorded-format fixtures; physical DualSense, ViGEmBus output, in-game behavior, a real ONNX model and Elgato require hardware validation. SHA256SUMS.txt verifies the downloadable executable and source archive.
