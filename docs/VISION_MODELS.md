# Vision model notes

The included default is YOLOX Nano, embedded in the executable. It uses 416×416 letterboxed BGR input and YOLOX grid decoding; person is COCO class 0. `setup-model.ps1` verifies the pinned official model. See Models/README.md for source and Apache-2.0 license.

The optional custom `YoloOnnxDetector` expects a YOLOv8 detection export with COCO class 0 representing `person` and output shaped `[1, attributes, detections]`. Export at 640×640 without built-in NMS.

Vision Aim intentionally does not train or infer friendly-versus-hostile allegiance. A generic person or head detector cannot make that distinction reliably. The local FOV and hold-to-activate behavior reduce unintended selection, but the user is responsible for where the crosshair is placed.

## Adding a pose/keypoint estimator

Implement `ITargetPointEstimator` and return a point in full-frame pixel coordinates. A pose implementation should prefer:

- Head: mean of available nose, eye, and ear keypoints, with confidence gating.
- Upper Torso: midpoint between confident shoulder keypoints.
- Center Mass: midpoint between shoulder and hip centers.

If the required keypoints are not confident, fall back to `ProportionalTargetPointEstimator`. Inject the implementation when `VisionAimEngine` is constructed in `MainWindow.xaml.cs`. The FOV gate, nearest-to-crosshair selection, lock, release, smoothing, speed cap, and overlay require no changes.

## Adding another detector

Implement `IObjectDetector`. Return person boxes relative to the bitmap supplied to `Detect`; the engine supplies only the crosshair-local crop and translates boxes back into full-frame coordinates. Apply model-specific preprocessing and non-maximum suppression inside the detector.
