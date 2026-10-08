"""Cut image-to-3D inputs out of the approved concept renders, by the manifest's regions.

    blender --background --factory-startup --python-exit-code 1
        --python tools/blender/concept_inputs.py --
        [--manifest art/manifests/creature/imp.yaml] --out build/modern/creature/imp/candidates/inputs

Each region (front, side, rear, full_body, face_closeup, pickaxe) is cropped, padded to a square
with the concept's own background colour (sampled at the crop's corners) and written as PNG,
with `inputs.json` recording the source file, its SHA-256, the region and each output's
SHA-256. Nothing is uploaded; which inputs go to which service is the trial plan's decision
(docs/plans/2026-10-07-imp-image-to-3d-trial.md). Pixels are never altered beyond the crop and
the padding: the rejected belly plate and the sack are still in them.
"""
import argparse
import hashlib
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import evaluate_candidate   # noqa: E402  (manifest_regions)
import sheets               # noqa: E402


def sha256(path):
    with open(path, "rb") as stream:
        return hashlib.sha256(stream.read()).hexdigest()


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="concept_inputs.py")
    parser.add_argument("--manifest", default="art/manifests/creature/imp.yaml")
    parser.add_argument("--out", required=True)
    args = parser.parse_args(argv)
    manifest, out = os.path.abspath(args.manifest), os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    record = []
    for concept in evaluate_candidate.manifest_regions(manifest):
        image = sheets.load(concept["file"])
        for region, (x0, y0, x1, y1) in concept["regions"].items():
            crop = image[y0:y1, x0:x1]
            corners = np.stack([crop[2, 2], crop[2, -3], crop[-3, 2], crop[-3, -3]])
            background = np.median(corners, axis=0)
            side = max(crop.shape[:2])
            square = np.empty((side, side, 4), dtype=np.float32)
            square[:] = background
            top, left = (side - crop.shape[0]) // 2, (side - crop.shape[1]) // 2
            square[top:top + crop.shape[0], left:left + crop.shape[1]] = crop
            path = sheets.save(square, os.path.join(out, f"{region}.png"))
            record.append({"output": os.path.basename(path), "sha256": sha256(path), "size": [side, side],
                           "source": concept["file"], "source_sha256": sha256(concept["file"]),
                           "region": [x0, y0, x1, y1]})
            print(f"{region:<13} {side}x{side} from {os.path.basename(concept['file'])} {[x0, y0, x1, y1]}")
    with open(os.path.join(out, "inputs.json"), "w", encoding="utf-8") as stream:
        json.dump(record, stream, indent=2)


if __name__ == "__main__":
    main()
