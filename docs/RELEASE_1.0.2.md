ControllerLab Pro 1.0.2 fixes excessive jitter and removes the separate model setup requirement.

- Jitter only runs while L2 is held above 35%; releasing L2 stops it immediately.
- Strength defaults to 0.5% and is capped at 3%, including old saved profiles. The previous maximum was 30% and caused substantial camera movement. Start low; your game's sensitivity affects visible motion.
- Removed all app deadzone and anti-deadzone controls/processing. Use the game's own deadzone settings. Sensitivity adjustment remains.
- Includes an official YOLOX Nano person detector embedded inside the executable, with Apache-2.0 attribution and license. No separate model download or models folder is required.
- The old missing default YOLOv8 path migrates to the included detector. Optional custom YOLOv8 ONNX files remain supported.
- Prevented profile-loading events from overwriting saved values.

Download **ControllerLabPro.exe**. Close the old app, open this version, start the controller, and select the virtual Xbox game output.

For Vision: choose **USE INCLUDED DETECTOR**, select your display, enable Vision, click **APPLY / RESTART VISION**, and hold L2. A generic person detector cannot distinguish teammates from enemies, and detection quality varies by game. Offline/story-mode use only.

Validation: zero build warnings/errors; 12/12 regression checks, including genuine embedded-model inference on an image containing people, blank-frame inference, jitter ADS gating/release, legacy strength limits and no deadzone processing. Physical controller/in-game behavior still requires testing on your PC. Executable/source download hashes are in SHA256SUMS.txt.
