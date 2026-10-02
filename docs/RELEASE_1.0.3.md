ControllerLab Pro 1.0.3 improves vision correction, simplifies the controls and includes a larger detector.

- Strength controls correction gain and maximum movement. Smoothness controls a time-based easing response, consistent across detector frame rates.
- The movement panel has just Strength and Smoothness. Confidence/radius settings live in a collapsed Advanced section.
- Controller output updates every 8 ms independently of HID reports/inference. The panel shows processed stick values and report counts, rather than implying a red rectangle confirms in-game movement.
- Correction stops on L2 release, stale vision expires after 250 ms, and stale input produces neutral controls after 500 ms.
- YOLOX-S is now the stronger included ONNX detector; Nano remains available as a faster option. Custom YOLOv8 ONNX files remain optional. Both official models are embedded and hash verified.
- Jitter is labeled experimental, with no verified aim-assist benefit, and is suppressed during Vision Aim so it cannot compete with correction.

Download **ControllerLabPro.exe**, close the old app, and start this version before the game. Choose **Stronger included ONNX detector**, enable Vision, select your game display, then **APPLY / RESTART VISION**. Hold L2 near a visible person. Higher Strength increases movement; lower Smoothness reacts faster.

The game must use the app's virtual Xbox controller. Its own right-stick deadzone may ignore small corrections; the app does not add deadzone processing. If the panel's processed output changes but the game does not move, verify controller routing and the game's deadzone settings.

A red rectangle marks the selected visible person. The detector cannot distinguish teammates from enemies or locate people hidden behind walls. No through-wall tracking is included. Offline/story-mode use only.

Validation: zero build warnings/errors and 14/14 checks, including genuine larger-model inference, FPS-independent smoothing, correction reaching the controller pipeline, timed output without new HID reports, L2 release and stale-data cleanup, and convergence in a simulated aiming loop. This is not a verified Call of Duty in-game test or a guarantee of aim improvement/human-looking movement. Download checksums are in SHA256SUMS.txt.
