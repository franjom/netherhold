"""Measure red wall-marker centres in the owner's native camera recording.

Requires Pillow, NumPy and SciPy. Native captures stay outside the checkout.
Usage: python compare-camera.py NATIVE_CAPTURE_DIR REMAKE_CAMERA_CAPTURE_DIR
Prints JSON; redirect to a new evidence file, never over an existing capture.
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


def centres(path, seeds, radius):
    pixels = np.asarray(Image.open(path).convert("RGB"), dtype=float)
    found = []
    for x, y in seeds:
        left, top = max(0, int(x) - radius), max(0, int(y) - radius)
        patch = pixels[top:int(y) + radius + 1, left:int(x) + radius + 1]
        weight = np.maximum(0, patch[..., 0] - 1.7 * patch[..., 1] - .3 * patch[..., 2] - 5)
        labels, count = ndimage.label(weight > 6)
        candidates = []
        for label in range(1, count + 1):
            w = np.where(labels == label, weight, 0)
            if w.sum() < 40:
                continue
            yy, xx = np.indices(w.shape)
            centre = np.array([left + (xx * w).sum() / w.sum(), top + (yy * w).sum() / w.sum()])
            candidates.append(centre)
        if not candidates:
            raise ValueError(f"No red marker near {(x, y)} in {path}")
        found.append(min(candidates, key=lambda p: np.linalg.norm(p - [x, y])))
    return np.array(found)


native, remake = map(Path, sys.argv[1:3])
samples = [
    (5, 40960, 19, [[608, 311], [724, 404], [543, 573], [918, 245],
                    [977, 197], [1026, 154], [812, 176], [1156, 184]]),
    (10, 53247, 15, [[722, 380], [615, 475], [862, 271], [913, 233],
                     [955, 197], [789, 217], [1046, 224]]),
]
report = {"native_game_rectangle": [180, 0, 1080, 810], "native_game_resolution": [1600, 1200],
          "focus_assumption": [14.5, 1, -24.5], "samples": []}
for seconds, zoom, radius, seeds in samples:
    original = (centres(native / f"frame-{seconds}.png", seeds, radius) - [180, 0]) / .675
    modern = centres(remake / f"gold-zoom-{zoom}.png", (np.array(seeds) - [180, 0]) / .675, 30)
    delta = modern - original
    report["samples"].append({"frame_seconds": seconds, "zoom_raw": zoom,
        "native_centres": original.tolist(), "remake_centres": modern.tolist(),
        "rms_per_axis_pixels": float(np.sqrt(np.mean(delta ** 2))),
        "mean_delta_pixels": delta.mean(axis=0).tolist(),
        "maximum_radial_error_pixels": float(np.linalg.norm(delta, axis=1).max())})
print(json.dumps(report, indent=2))
