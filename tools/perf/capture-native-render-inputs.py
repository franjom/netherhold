#!/usr/bin/env python3
# Copyright (C) 2026 Franjo Misetic
# SPDX-License-Identifier: GPL-3.0-or-later
"""Read selected renderer inputs from an already running, unmodified DKII.EXE.

No process writes, injection, input automation or settings changes are performed.
Addresses refer to the pinned retail binary; see classic-lighting.md section 8.
"""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import struct


class Reader:
    def __init__(self, pid):
        self.api = ctypes.WinDLL("kernel32", use_last_error=True)
        self.api.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
        self.api.OpenProcess.restype = wintypes.HANDLE
        self.api.ReadProcessMemory.argtypes = (wintypes.HANDLE, ctypes.c_void_p,
            ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t))
        self.api.ReadProcessMemory.restype = wintypes.BOOL
        self.api.CloseHandle.argtypes = (wintypes.HANDLE,)
        self.api.QueryFullProcessImageNameW.argtypes = (wintypes.HANDLE, wintypes.DWORD,
            wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD))
        self.handle = self.api.OpenProcess(0x1010, False, pid)  # QUERY_LIMITED_INFORMATION | VM_READ
        if not self.handle:
            raise ctypes.WinError(ctypes.get_last_error())

    def read(self, address, size):
        buffer = ctypes.create_string_buffer(size)
        received = ctypes.c_size_t()
        if not self.api.ReadProcessMemory(self.handle, address, buffer, size, ctypes.byref(received)):
            raise ctypes.WinError(ctypes.get_last_error())
        if received.value != size:
            raise RuntimeError("Incomplete native input read")
        return buffer.raw

    def image(self):
        path = ctypes.create_unicode_buffer(32768)
        length = wintypes.DWORD(len(path))
        if not self.api.QueryFullProcessImageNameW(self.handle, 0, path, ctypes.byref(length)):
            raise ctypes.WinError(ctypes.get_last_error())
        return Path(path.value)

    def close(self):
        self.api.CloseHandle(self.handle)


def capture_heart_inputs(reader):
    objects = []
    lights = []
    errors = []
    thing_slots = struct.unpack("<4096I", reader.read(0x6EE540, 4096 * 4))
    view_slots = struct.unpack("<4096I", reader.read(0x6D6458, 4096 * 4))
    for slot, address in enumerate(thing_slots):
        if not slot or not address:
            continue
        try:
            prefix = reader.read(address, 0x16)
            if struct.unpack_from("<I", prefix, 0x12)[0] != 2:
                continue
            raw = reader.read(address, 0x134)
            if raw[0x12C] not in (13, 86, 88, 94):
                continue
            objects.append({"slot": slot, "address": hex(address), "type": raw[0x12C],
                "tag": struct.unpack_from("<H", raw, 4)[0],
                "position": struct.unpack_from("<3i", raw, 0x1A),
                "previous_position": struct.unpack_from("<3i", raw, 0xF6),
                "heading": struct.unpack_from("<H", raw, 0xF4)[0],
                "scale": struct.unpack_from("<2i", raw, 0x48),
                "render_rgb_alpha": list(raw[0x8C:0x90]),
                "render_offset": struct.unpack_from("<3h", raw, 0x95),
                "definition": hex(struct.unpack_from("<I", raw, 0x12D)[0]),
                "light_slot": struct.unpack_from("<H", raw, 0x131)[0]})
            definition = struct.unpack_from("<I", raw, 0x12D)[0]
            objects[-1]["light_descriptor_hex"] = reader.read(definition + 0xD8, 24).hex()
            resource = struct.unpack_from("<I", raw, 0x6C)[0]
            objects[-1]["render_resource"] = hex(resource)
            if resource:
                objects[-1]["render_resource_hex"] = reader.read(resource, 27).hex()
        except OSError as error:
            errors.append({"thing_slot": slot, "error": str(error)})
    for slot, address in enumerate(view_slots):
        if not slot or not address:
            continue
        try:
            raw = reader.read(address, 0xF8)
            if struct.unpack_from("<I", raw, 0xF4)[0] != 2:
                continue
            raw = reader.read(address, 0x146)
            lights.append({"slot": slot, "address": hex(address),
                "position": struct.unpack_from("<3i", raw, 0xD6),
                "previous_position": struct.unpack_from("<3i", raw, 0x105),
                "mode": struct.unpack_from("<I", raw, 0xE4)[0],
                "rgb": struct.unpack_from("<3H", raw, 0x111),
                "radius": struct.unpack_from("<I", raw, 0x117)[0],
                "parent_tag": struct.unpack_from("<H", raw, 0x12E)[0],
                "flags": hex(struct.unpack_from("<I", raw, 0x134)[0]),
                "intensity": raw[0x145]})
        except OSError as error:
            errors.append({"view_slot": slot, "error": str(error)})
    return {"objects": objects, "lights": lights, "read_errors": errors}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int)
    parser.add_argument("--exe", type=Path)
    parser.add_argument("--expected-sha256", help="Digest of the retail executable used for the Ghidra address records")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--heart-inputs", action="store_true", help="Capture Heart object and engine-light records")
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("Windows is required")
    if not args.self_test and (args.pid is None or args.exe is None or args.output is None or args.expected_sha256 is None):
        parser.error("Supply --pid, --exe, --expected-sha256 and --output for the pinned retail executable")
    reader = Reader(os.getpid() if args.self_test else args.pid)
    try:
        if args.self_test:
            payload = ctypes.create_string_buffer(b"DK2 read-only renderer input probe")
            if reader.read(ctypes.addressof(payload), len(payload)) != payload.raw:
                raise RuntimeError("Read-only self-test failed")
            print("Read-only process-memory self-test passed")
            return
        image = reader.image()
        if image.resolve() != args.exe.resolve():
            raise RuntimeError("Process executable does not match --exe")
        data = image.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if digest != args.expected_sha256.lower():
            raise RuntimeError("Executable hash does not match the supplied Ghidra reference digest")
        if len(data) < 64 or data[:2] != b"MZ":
            raise RuntimeError("Invalid executable DOS header")
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        if pe < 64 or pe + 120 > len(data):
            raise RuntimeError("Invalid executable PE header offset")
        if data[pe:pe + 4] != b"PE\0\0" or struct.unpack_from("<H", data, pe + 24)[0] != 0x10B:
            raise RuntimeError("The pinned executable must be PE32")
        base = struct.unpack_from("<I", data, pe + 52)[0]
        if base != 0x400000 or reader.read(base, 64) != data[:64]:
            raise RuntimeError("Unexpected executable load address/header")
        raw = reader.read(0x760A98, 12)
        ambient = struct.unpack("<3f", raw)
        result = {"pid": args.pid, "executable": str(image),
            "sha256": digest, "image_base": hex(base),
            "ambient_raw_rgb": ambient, "ambient_normalized_rgb": [v / 255 for v in ambient],
            "ambient_stored_setting": struct.unpack("<I", reader.read(0x759AC4, 4))[0],
            "cheap_lighting_setting": struct.unpack("<I", reader.read(0x759AF8, 4))[0],
            "render_option_5c": struct.unpack("<I", reader.read(0x759AD4, 4))[0]}
        try:
            bridge = struct.unpack("<I", reader.read(0x6DA458, 4))[0]
            world = struct.unpack("<I", reader.read(bridge + 0x9E, 4))[0]
            vtable = struct.unpack("<I", reader.read(world, 4))[0]
            result["world_color_override_getter"] = hex(struct.unpack("<I", reader.read(vtable + 0xC4, 4))[0])
            if result["world_color_override_getter"] == "0x443100":
                result["world_color_override_player"] = struct.unpack("<H", reader.read(world + 0xE, 2))[0]
            result["player_palette_rgba"] = [list(row) for row in struct.iter_unpack("<4B", reader.read(0x6E5058, 96))]
            result["world_palette_fields_290_2af"] = reader.read(world + 0x290, 32).hex()
        except OSError as error:
            result["world_color_inputs_error"] = str(error)
        if args.heart_inputs:
            result["heart_inputs"] = capture_heart_inputs(reader)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        print(json.dumps(result, allow_nan=False))
    finally:
        reader.close()


if __name__ == "__main__":
    main()
