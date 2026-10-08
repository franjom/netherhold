"""Copy one subject's converted GLBs into a reference set, and record where they came from.

    python tools/blender/stage_reference.py --converted build/converted-assets
        --log build/convert.log --subject creature.imp --out build/reference/imp/source
        [--key creature.imp.animation.idle ...] [--install <DK2 dir>]

`dk2tools convert` has no subject filter: it writes the whole catalogue. This picks out
one subject's keys from the conversion log, keeps one GLB per distinct source KMF (most
of the Imp's 40 keys reuse Imp_Run), and copies each GLB with every file it references,
keeping the converter's relative layout so its texture URIs resolve unchanged.

Writes `sources.json` beside the copies: the converter command, git commit, each clip's
catalogue keys, source KMF, SHA-256 of every copied file, and of Meshes.WAD and
Creatures.kwd when --install is given. Plain CPython; no Blender needed. The copies are
EA-derived and stay under build/ (ADR-0242).
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import urllib.parse

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
# `PRODUCED creature.imp.animation.idle -> creature\imp\animation\idle.glb (animation KMF from Imp_Idle1.kmf)`
PRODUCED = re.compile(r"^PRODUCED (\S+) -> (\S+) \((.*) from (.+?)\)\s*$")
MISSING = re.compile(r"^MISSING\s+(\S+) -> (.*)$")


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def glb_uris(path):
    """Every external URI a GLB names: images, buffers and DK2.Convert's material extras."""
    with open(path, "rb") as stream:
        data = stream.read()
    magic, _, _ = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        raise ValueError(f"{path} is not a GLB")
    length, kind = struct.unpack_from("<I4s", data, 12)
    if kind != b"JSON":
        raise ValueError(f"{path}: first chunk is not JSON")
    document = json.loads(data[20:20 + length])
    uris = [entry["uri"] for key in ("images", "buffers") for entry in document.get(key, []) if "uri" in entry]
    for material in document.get("materials", []):
        extras = material.get("extras", {})
        uris += extras.get("dk2TextureAlternatives", [])
        if "dk2EnvironmentTexture" in extras:
            uris.append(extras["dk2EnvironmentTexture"])
    return sorted(set(uris))


def git_commit():
    try:
        return subprocess.run(["git", "-C", ROOT, "rev-parse", "HEAD"], capture_output=True,
                              text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--converted", required=True, help="dk2tools convert output root")
    parser.add_argument("--log", required=True, help="the stdout of that dk2tools convert run")
    parser.add_argument("--subject", required=True, help="AssetRef prefix, e.g. creature.imp")
    parser.add_argument("--out", required=True, help="reference source directory to (re)create")
    parser.add_argument("--key", action="append", default=[],
                        help="stage only these keys (default: every produced key of the subject)")
    parser.add_argument("--install", help="DK2 install, to hash the archives the clips came from")
    parser.add_argument("--command", default="dk2tools convert --out converted-assets/",
                        help="the converter command, recorded verbatim")
    args = parser.parse_args()

    prefix = args.subject + "."
    produced, missing = {}, {}
    with open(args.log, encoding="utf-8", errors="replace") as log:
        for line in log:
            if match := PRODUCED.match(line):
                key, relative, detail, source = match.groups()
                if key.startswith(prefix):
                    produced[key] = (relative.replace("\\", "/"), detail, source)
            elif match := MISSING.match(line):
                if match.group(1).startswith(prefix):
                    missing[match.group(1)] = match.group(2).strip()
    if not produced:
        sys.exit(f"no PRODUCED keys for {args.subject} in {args.log}")
    unknown = [key for key in args.key if key not in produced]
    if unknown:
        sys.exit(f"not produced by this conversion: {', '.join(unknown)}")
    selected = args.key or sorted(produced)

    # One clip per source KMF: the first selected key (in the order given) supplies the file.
    clips = {}
    for key in selected:
        relative, detail, source = produced[key]
        clip = clips.setdefault(source, {"source": source, "glb": relative, "keys": [], "detail": detail})
        clip["keys"].append(key)

    if os.path.isdir(args.out):
        shutil.rmtree(args.out)
    copied = {}
    for clip in clips.values():
        glb = os.path.join(args.converted, clip["glb"])
        files = [clip["glb"]] + [
            os.path.normpath(os.path.join(os.path.dirname(clip["glb"]), urllib.parse.unquote(uri))).replace("\\", "/")
            for uri in glb_uris(glb)]
        clip["files"] = files
        for relative in files:
            source_path = os.path.join(args.converted, relative)
            if relative.startswith("../") or os.path.isabs(relative):
                sys.exit(f"{clip['glb']} references a file outside the conversion root: {relative}")
            if not os.path.isfile(source_path):
                sys.exit(f"{clip['glb']} references a missing file: {relative}")
            target = os.path.join(args.out, relative)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copyfile(source_path, target)
            copied[relative] = sha256(target)

    archives = {}
    if args.install:
        for relative in ("Data/Meshes.WAD", "Data/editor/Creatures.kwd"):
            path = os.path.join(args.install, relative)
            archives[relative] = sha256(path) if os.path.isfile(path) else None

    record = {
        "subject": args.subject,
        "staged": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
        "gitCommit": git_commit(),
        "converterCommand": args.command,
        "converterLog": os.path.abspath(args.log),
        "archives": archives,
        "clips": [{**clip, "name": os.path.splitext(clip["source"])[0]} for clip in clips.values()],
        "missingKeys": missing,
        "sha256": dict(sorted(copied.items())),
    }
    with open(os.path.join(args.out, "sources.json"), "w", encoding="utf-8") as stream:
        json.dump(record, stream, indent=2)
    for clip in record["clips"]:
        print(f"{clip['name']:<14} {clip['glb']}  ({len(clip['keys'])} keys)")
    print(f"-- {len(clips)} clips, {len(copied)} files -> {args.out}; {len(missing)} keys missing")


if __name__ == "__main__":
    main()
