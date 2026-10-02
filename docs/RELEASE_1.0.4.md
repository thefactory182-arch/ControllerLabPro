ControllerLab Pro 1.0.4 increases the upper range of Vision Aim strength.

- Default 65% strength keeps its previous response. Above 65%, correction gain increases rapidly; maximum gain is 4.33 times the previous maximum before clipping, and allows full right-stick output instead of the previous 75% limit.
- Correction still tapers to zero at the selected aim point, uses time-based Smoothness, and stops on L2 release. No deadzone processing was added.
- Advanced detection settings now explain how a smaller FOV enlarges the area near the crosshair for detecting small distant people. Detection/model behavior itself is unchanged.

Download ControllerLabPro.exe, close the previous app, and start this version before the game. Select the stronger included ONNX detector, your display, upper torso, and Apply / Restart Vision. Try 80% strength first, then increase; maximum with zero smoothness can overshoot depending on game sensitivity. If needed, raise Smoothness.

For distant visible people, try FOV radius 10–15% and aim closer to them. The narrower crop sacrifices acquisition area; it cannot recover detail absent from the screen. Detection has no verified range in meters and cannot distinguish enemies from teammates or see through walls. Offline/story-mode use only.

Validation: 15 functional checks, including stronger correction for a simulated tiny visible target, full-stick limit, zero centered correction, default convergence, smoothing, real included-model inference and controller output gating. Actual Call of Duty movement and distant in-game detection have not been verified. Checksums are included in SHA256SUMS.txt.
