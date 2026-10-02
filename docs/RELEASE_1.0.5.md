ControllerLab Pro 1.0.5 adds an optional Strong target follow mode for offline story play.

- Enable Strong target follow in Vision Aim for rapidly increasing correction near the aim point and faster smoothing response. Strength still controls maximum correction; Smoothness eases movement. The normal mode is unchanged.
- Clears easing on a direction reversal so accumulated movement does not keep turning away after crossing the target. Clears correction on a missed detection instead of continuing a previous turn. L2 release, stale vision/input cleanup and manual stick input remain supported.
- Profiles save the new option. Existing profiles keep normal mode until you enable it.

Close the old app, download ControllerLabPro.exe, start controller before the game, choose your display and the stronger included ONNX detector, enable Vision and Strong target follow, choose Upper Torso, then Apply / Restart Vision. Start with Strength 90%, Smoothness 25%; hold L2 near a visible person. Increase strength or lower smoothness for a faster response; reverse those adjustments if it overshoots.

The detector identifies visible people, including teammates and civilians; it does not understand enemy allegiance. It cannot see through walls. Game sensitivity, deadzones, detection latency and target visibility affect movement; this is not a guaranteed lock-on or tested Call of Duty aimbot. No game memory access, automatic firing or controller deadzone processing was added.

Validation: 17 functional checks including strong near-center acquisition, reversal, missed detection, zero strength, moving-target convergence in 60/120 Hz simulations, genuine ONNX inference and controller output gating. Actual in-game performance has not been verified. Checksums included in SHA256SUMS.txt.
