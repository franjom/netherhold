"""Writes src/DK2.Simulation/StateCopy.g.cs: a CopyFrom (and a Clone) for each simulation state class,
derived from the class's own field and auto-property declarations, so the presented world (ADR-0693)
can be brought up to date in place. Rerun after adding a field to one of the classes below; the
reflection test StateCopyTests.CopyCoversEveryField fails until you do.

    python tools/state-copy/generate.py

Rules, per declaration: a settable field or auto-property is assigned; an array is Array.Copy'd; a List
or Dictionary is cleared and refilled; an init-only property is left to Clone (MemberwiseClone); an
explicit-bodied property is skipped, since its backing field is copied as a field; a nested state
object named in `nested` has its own CopyFrom called. Anything else is reported and must be handled.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SIM = ROOT / "src" / "DK2.Simulation"
OUT = SIM / "StateCopy.g.cs"

# name -> (file, options). key: the member that identifies an instance across copies (None: no Clone/key).
# skip: members deliberately not copied. nested: members with their own CopyFrom. ctor: how Clone creates
# the instance ("memberwise" for MemberwiseClone, else an expression on `this`).
CLASSES: dict[str, dict] = {
    "CreatureState": dict(file="CreatureState.cs", key="Id", skip={"LinkWorld"}, nested={"Body"}, ctor="new CreatureState()"),
    "ObjectState": dict(file="ObjectState.cs", key="Id", skip={"LinkWorld"}, ctor="new ObjectState()"),
    "ShotState": dict(file="ShotState.cs", key="Id", skip={"LinkWorld"}, ctor="new ShotState()"),
    "DoorState": dict(file="DoorState.cs", key="Id", ctor="memberwise"),
    "TrapState": dict(file="TrapState.cs", key="Id", ctor="memberwise"),
    "ActionPointState": dict(file="ActionPointState.cs", key="Id", ctor="memberwise"),
    "HeroPartyState": dict(file="HeroPartyState.cs", key=None, ctor="memberwise"),
    "EffectGeneratorState": dict(file="EffectGeneratorState.cs", key=None, ctor="memberwise"),
    "TempleSacrificeState": dict(file="TempleSacrificeState.cs", key="AnchorTileIndex", skip={"AnchorTileIndex", "OwnerPlayerId"},
                                 ctor="new TempleSacrificeState(AnchorTileIndex, OwnerPlayerId)"),
    "GraveyardState": dict(file="GraveyardState.cs", key="AnchorTileIndex", skip={"AnchorTileIndex", "OwnerPlayerId"},
                           ctor="new GraveyardState(AnchorTileIndex, OwnerPlayerId)"),
    "SteeringBody": dict(file="Steering/SteeringBody.cs", namespace="DK2.Simulation.Steering"),
    "TriggerState": dict(file="TriggerState.cs"),
    "PlayerMessageQueue": dict(file="PlayerMessages.cs"),
    "TileHighlightState": dict(file="TileHighlightState.cs"),
    "CreatureSlotPool": dict(file="CreatureSlotPool.cs"),
}

ACCESS = ("public", "internal", "private", "protected")
MODIFIERS = ACCESS + ("readonly", "required", "static", "const", "override", "new", "volatile", "sealed", "virtual")


def class_body(src: str, name: str) -> str:
    start = re.search(r"^(public|internal) (sealed |readonly |partial )*(record )?(struct|class) " + name + r"\b", src, re.M)
    if not start:
        sys.exit(f"{name}: class declaration not found")
    depth, opened, end = 0, False, len(src)
    for i in range(start.start(), len(src)):
        c = src[i]
        if c == "{":
            depth += 1
            opened = True
        elif c == "}":
            depth -= 1
            if opened and depth == 0:
                end = i
                break
    return src[start.start():end]


def read_type(text: str, i: int) -> tuple[str, int]:
    depth, start = 0, i
    while i < len(text):
        c = text[i]
        if c in "<(":
            depth += 1
        elif c in ">)":
            depth -= 1
        elif c.isspace() and depth == 0:
            break
        i += 1
    return text[start:i], i


def balanced(text: str, i: int) -> int:
    """Index just past the brace block opening at text[i]."""
    depth = 0
    while i < len(text):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return i


def declarations(body: str):
    """Yields (mods, type, name, kind) for each member declared at the class's own indentation."""
    for m in re.finditer(r"^    (?=(?:public|internal|private|protected)\b)", body, re.M):
        i = m.end()
        mods = []
        while True:
            word = re.match(r"\w+", body[i:])
            if not word or word.group() not in MODIFIERS:
                break
            mods.append(word.group())
            i += word.end()
            while body[i].isspace():
                i += 1
        typ, i = read_type(body, i)
        if typ in ("class", "struct", "enum", "record", "event", "delegate", "interface") or typ.startswith("event"):
            continue
        while i < len(body) and body[i].isspace():
            i += 1
        name = re.match(r"\w+", body[i:])
        if not name:
            continue
        name = name.group()
        i += len(name)
        names = [name]
        while True:  # `private int _posX, _posY;` declares two fields
            while i < len(body) and body[i].isspace():
                i += 1
            if body[i] != ",":
                break
            i += 1
            while body[i].isspace():
                i += 1
            more = re.match(r"\w+", body[i:]).group()
            names.append(more)
            i += len(more)
        rest = body[i:]
        if rest.startswith("("):
            continue  # a constructor or a method
        if "static" in mods or "const" in mods:
            continue
        if rest.startswith("=>"):
            kind = "computed"
        elif rest.startswith("{"):
            block = body[i:balanced(body, i)]
            inner = block[1:-1]
            if "{" in inner or "=>" in inner:
                kind = "explicit"
            else:
                words = set(re.findall(r"\w+", inner))
                if "set" in words:
                    kind = "settable"
                elif "init" in words:
                    kind = "init"
                else:
                    kind = "get-only"
        else:
            kind = "field"
        for each in names:
            yield mods, typ, each, kind


def is_array(typ: str) -> bool:
    return typ.endswith("[]")


def is_list(typ: str) -> bool:
    return typ.startswith("List<")


def is_dictionary(typ: str) -> bool:
    return typ.startswith("Dictionary<")


def generate(name: str, spec: dict) -> tuple[str, list[str]]:
    src = (SIM / spec["file"]).read_text(encoding="utf-8-sig")
    body = class_body(src, name)
    access = body.split()[0]
    skip = spec.get("skip", set())
    nested = spec.get("nested", set())
    lines, problems = [], []
    for mods, typ, member, kind in declarations(body):
        if member in skip or kind in ("computed", "explicit", "init"):
            continue
        if member in nested:
            lines.append(f"{member}.CopyFrom(other.{member});")
        elif is_array(typ):
            lines.append(f"Array.Copy(other.{member}, {member}, {member}.Length);")
        elif is_list(typ):
            lines.append(f"{member}.Clear();")
            lines.append(f"{member}.AddRange(other.{member});")
        elif is_dictionary(typ):
            lines.append(f"{member}.Clear();")
            lines.append(f"foreach (var pair in other.{member}) {member}.Add(pair.Key, pair.Value);")
        elif kind == "get-only" or (kind == "field" and "readonly" in mods):
            if "Memory<" in typ or typ.startswith("IReadOnlyList<") or typ == "string":
                continue  # immutable, shared by Clone
            problems.append(f"{name}.{member} ({typ}, {kind}{' readonly' if 'readonly' in mods else ''}) cannot be assigned")
        elif re.fullmatch(r"(System\.)?(Action|Func)(<.*>)?\??", typ):
            problems.append(f"{name}.{member} is a delegate")
        else:
            lines.append(f"{member} = other.{member};")

    key = spec.get("key")
    ctor = spec.get("ctor")
    out = [f"{access} sealed partial class {name}" + (f" : IStateCopy<{name}>" if ctor else ""), "{"]
    if ctor:
        out.append(f"    int IStateCopy<{name}>.Key => {key};" if key else f"    int IStateCopy<{name}>.Key => 0;")
        out.append("")
        if ctor == "memberwise":
            out.append(f"    public {name} Clone() => ({name})MemberwiseClone();")
        else:
            out.append(f"    public {name} Clone()")
            out.append("    {")
            out.append(f"        var copy = {ctor};")
            out.append("        copy.CopyFrom(this);")
            out.append("        return copy;")
            out.append("    }")
        out.append("")
    out.append(f"    public void CopyFrom({name} other)")
    out.append("    {")
    out.extend(f"        {line}" for line in lines)
    out.append("    }")
    out.append("}")
    return "\n".join(out), problems


def main() -> None:
    header = """// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

// <auto-generated>
// Written by tools/state-copy/generate.py from each class's field declarations. Do not edit by hand:
// add the field to its class and rerun the script. StateCopyTests holds every CopyFrom to its fields.
// </auto-generated>

#nullable enable
#pragma warning disable CS1591

namespace DK2.Simulation
{
    /// <summary>A state object the presented world keeps a copy of, updated in place by key (ADR-0693).</summary>
    public interface IStateCopy<T> where T : class
    {
        /// <summary>What identifies the instance across copies: the thing id, or a room anchor.</summary>
        int Key { get; }

        void CopyFrom(T other);

        T Clone();
    }
}
"""
    sections, problems = [], []
    for name, spec in CLASSES.items():
        code, found = generate(name, spec)
        problems.extend(found)
        ns = spec.get("namespace", "DK2.Simulation")
        sections.append(f"namespace {ns}\n{{\n" + "\n".join("    " + l if l else "" for l in code.split("\n")) + "\n}")
    if problems:
        sys.exit("unhandled members:\n  " + "\n  ".join(problems))
    OUT.write_text(header + "\n" + "\n\n".join(sections) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
