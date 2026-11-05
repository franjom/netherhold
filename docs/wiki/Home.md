# Netherhold

A work-in-progress reimplementation of **Dungeon Keeper 2** in C#, with a Unity
presentation layer. The goal is to reproduce the original game's observable
behaviour, with the simulation kept independent of the renderer.

This is a development and research project, not a finished replacement for the
original game. Campaign compatibility, gameplay fidelity and the classic Unity
presentation are still being verified.

> **This wiki is a guide, not the source of truth.** The authoritative documents
> live in the repository under [`docs/`](https://github.com/franjom/netherhold/tree/master/docs),
> where they are versioned and checked with the code. Each wiki page links to them.

## Screenshots

<p align="center">
<img src="https://github.com/franjom/netherhold/raw/master/screenshots/image_main_menu.jpg" alt="Main menu" width="49%">
<img src="https://github.com/franjom/netherhold/raw/master/screenshots/image_level_1_1.jpg" alt="Level 1" width="49%">
</p>

More in the [README](https://github.com/franjom/netherhold#screenshots).

## How the project works

- **The original binary is the primary source.** Behaviour is researched in
  `DKII.EXE` with Ghidra and in the shipped data files before it is
  implemented. It is not reconstructed from memory of play.
- **Every claim carries a confidence level:** CONFIRMED (seen in the running
  game), STRONGLY INFERRED (read in the binary or the shipped data), HYPOTHESIS
  or UNKNOWN; function records add DISPUTED ([[Glossary]]).
- **The simulation is deterministic.** It runs on integer ticks (4 per second,
  DK2's own rate), uses fixed-point maths and a seeded random source, and takes
  every balance number from the original game database.
- **Unity is presentation only.** No game rule or game state lives in the Unity
  project.

## Start here

| You are… | Start with | Then |
|---|---|---|
| **Curious about the project** | [[Charter]]: what it is for, and what "faithful" means | [[Status Matrix\|Status-Matrix]] for how far each system has got, or a system page such as [[Imps and Tile Work\|Imps-and-Tile-Work]] |
| **A C# contributor** | [[Solution Map\|Solution-Map]] and [[Determinism]] | [Build and test](https://github.com/franjom/netherhold#build-and-test), then the [contributor rules](https://github.com/franjom/netherhold/blob/master/AGENTS.md) |
| **A reverse engineer** | [[Legal and Clean Room\|Legal-and-Clean-Room]]: what may and may not cross from the binary | [[KB Coverage\|KB-Coverage]] and [[Open Questions\|Open-Questions]], and the [method](https://github.com/franjom/netherhold/blob/master/reverse-engineering/README.md) |
| **A Unity developer** | [Unity presentation](https://github.com/franjom/netherhold/blob/master/unity/README.md): the adapter, which holds no game rules | [Run the Unity presentation](https://github.com/franjom/netherhold#run-the-unity-presentation) and the [architecture](https://github.com/franjom/netherhold/blob/master/docs/ARCHITECTURE.md) |
| **A maintainer** | [Current state (HANDOFF)](https://github.com/franjom/netherhold/blob/master/docs/HANDOFF.md) | [[Decisions by System\|Decisions-by-System]], [[Simulation Read-Write Matrix\|Simulation-Read-Write-Matrix]] and the [plan](https://github.com/franjom/netherhold/blob/master/DK2_REIMPLEMENTATION_PLAN.md) |

Words you may not know are in the [[Glossary]]. Each system has a page in the
same shape (see the [[System Page Template\|System-Page-Template]]):
[[Imps and Tile Work\|Imps-and-Tile-Work]], [[Navigation]] and
[[Doors and Traps\|Doors-and-Traps]] so far.

## Requirements at a glance

- The .NET SDK selected by [`global.json`](https://github.com/franjom/netherhold/blob/master/global.json) (.NET 10).
- Optional: the Unity editor version pinned in
  [`ProjectVersion.txt`](https://github.com/franjom/netherhold/blob/master/unity/DK2.Unity/ProjectSettings/ProjectVersion.txt)
  for the presentation.
- Optional: your own original DK2 installation, pointed to by `DK2_DIR`, for
  campaign content, the headless harness and the install-dependent tests.
  Without it the solution still builds, and those tests are reported as skipped.

## Legal

Project source is licensed under
[GPL-3.0-or-later](https://github.com/franjom/netherhold/blob/master/LICENSE).
The repository contains **no original DK2 game files**. Everything the game
needs is read at run time from your own installation. Dungeon Keeper is a
trademark of its owner. This project is not affiliated with or endorsed by
Electronic Arts or Bullfrog Productions. See
[THIRD-PARTY-NOTICES.md](https://github.com/franjom/netherhold/blob/master/THIRD-PARTY-NOTICES.md).
