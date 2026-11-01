# levelgen: test levels for original-game experiments

When a question needs DK2 itself to answer it (an `experiments/` setup, an owner check, a
prediction from the binary), build a level that stages exactly that situation, play it in
DK2, and record the full state while it runs. First used for Q520, the Boulder, on 2026-11-01.
There were three steps:

1. **Build.** `levelgen` copies a shipped level, edits its map, things and variables, and
   saves it under a new name.
2. **Play.** DK2 loads the new name with `-LEVEL <name>`, and `dk2record.py` records every tick.
3. **Read.** `trace_capture.py` (or any `dk2capture.py` decoder) reads the capture.

## 1. Build

```
dotnet run --project tools/levelgen -c Release -- list                      # the recipes
dotnet run --project tools/levelgen -c Release -- survey                    # every shipped level: size, traps, heroes, overrides
dotnet run --project tools/levelgen -c Release -- show Secret4              # map as text, every thing, every variable override
dotnet run --project tools/levelgen -c Release -- build <recipe> <stage>    # writes <stage>/maps/<recipe>.kwd + 5 .kld, reads it back
```

The install comes from `DK2_DIR`, or `E:\GogGames\Dungeon Keeper 2` if it is unset. Nothing
is written to the install.

**A recipe** is an entry in `Recipes.cs`: a name, a shipped base level and the edits.
`LevelBuilder` wraps `DK2.Data.Editing.KwdLevelFileSet` (`src/DK2.Data/README.md`, "Editing"):

- `Paint(x, y, MapTile)` repaints a tile;
- `Things`, `EditThing<T>` and `RemoveThing` change placed things (indices shift after a removal);
- `InsertThing(row, image)` appends a thing over a template image, and `Template<T>(match)` finds one in
  any shipped level. Secret4 has no doors or keeper creatures; Circlet and Alcatraz do. A template's other
  fields come along: Circlet's Hero Sentry gives `NumberOfShots` 4 and `Unknown_000F` 115;
- `SetTriggerParameter(id, name, value)` changes one decoded trigger parameter (M7Traps lengthens Secret4's
  time limit, action 23 on trigger 65534, from 400 to 3600);
- `SetVariable(id, value)` changes one of the level's own overrides, and `GlobalValue(id)` reads the campaign value;
- `SaveAs` renames the files and re-points the LEVEL manifest at them.

`KwdLevelFileSet.EditRecord` refuses LEVEL path edits, so `SaveAs` rewrites them through
`KwdLevel.WriteRecord`, which accepts them when the count is unchanged.

**Keep every recipe once it has been played.** It is what rebuilds the level behind a capture;
`build Q520Corridors` reproduces the played files byte for byte. Edit by copying to a new name.

**Choosing a base level.** Start from the shipped level closest to the experiment:

- `Secret4` (41×41) is boulder golf: 11 Boulder traps owned by player 3, nine weak Dwarves.
- The skirmish maps (`Small`, `Spiral`, `U Turn`, `Warrens`) have no triggers at all.
- A campaign level has a tutorial or script that may get in the way.

Player 3 is the human keeper, player 1 the heroes, player 2 neutral. Read the base with `show`
first: **its `Variables.kld` overrides beat `GlobalVariables.kwd`.** Secret4 sets the boulder's
health to 10000 (not 4000), its damage to creatures to 100% (not 50%), and slap damage to 5. Reset
such overrides when the experiment is about campaign numbers (`Q520CorridorsGlobal`).

**Facts about the file format used here.**

- Map tiles are `MapTile(terrain, owner, flags, unknown)`. Shipped tiles carry flags 1.
- Terrain ids: 1 Impenetrable Rock (use it for test walls), 2 Rock, 8 Claimed Path, 9 Reinforced Wall, 14 Dungeon Heart.
- A placed creature's `InitialHealth` is a percentage of its level-scaled maximum (placed-things.md §3).
- A hero standing still takes `Objective` 23 with an action point on its own tile: move the point with the hero.
- Resizing the map does not move placed things.

## 2. Install and play

Copy the six files from `<stage>\maps` into `<install>\Data\editor\maps`, **under new names
only**: never overwrite a shipped file. They stay there only for the session (see below). Archive the same six files and the recipe's commit in
the run's evidence folder, which holds the capture too (AGENTS.md: `F:\DK2Evidence\` is add-only).

The recorder must be running before the game starts, and both must run elevated. From an
elevated PowerShell, as one line, with the run folder already created:

```powershell
Start-Process cmd -WorkingDirectory 'D:\DungeonKeeperRemake\reverse-engineering' -ArgumentList '/k python -u dk2record.py F:\DK2Evidence\captures\native\<capture-id>\<run>.dk2cap --map <W>x<H>'; Start-Sleep 3; Start-Process 'E:\GogGames\Dungeon Keeper 2\DKII-DX.exe' -ArgumentList '-LEVEL <recipe>' -WorkingDirectory 'E:\GogGames\Dungeon Keeper 2'
```

`cmd /k` keeps the recorder's summary on screen after DK2 exits. `--map` is the level's size
(`show` prints it). `MESSAGE.LOG` in the install records the command line DK2 ran with, so it
shows whether the level loaded. The recorder and its options are in
`reverse-engineering/README.md`, "Full-state captures".

**Remove the level from the install after the session**, once its six files are in the run's
evidence folder (check by SHA-256), and with the owner's approval. The install-gated tests treat every
file in `Data\editor\maps` as the shipped corpus of 66 levels. On 2026-11-01 four test levels left
installed failed 28 of them; `build` brings a level back byte for byte.

Lessons from Q520:

- **The run folder must exist before the recorder starts**, or nothing is written. On
  2026-11-01 a folder created from an agent's sandboxed Bash under `F:\DK2Evidence` did not
  persist. Create it with PowerShell, and list it before you hand over the command.
- **Ask the owner for what the capture cannot hold.** It stores state only, not sound, screen
  shake or anything else presented, so ask for those observations explicitly.
- **Give every case its own straight corridor.** In open terrain the player has to aim, and the
  cases mix.

## 3. Read the capture

```
python tools/levelgen/trace_capture.py F:\DK2Evidence\captures\native\<capture-id>\<run>.dk2cap [--object-type 74]
```

It prints one line per change: objects of the given type (position, roll, health, wear) and
every creature's health and state. For other fields, open the capture with
`dk2capture.Capture` and read `rec.u(offset, fmt)`. Field offsets belong in
`dk2capture.FIELDS`, each with its source.

## Levels built so far

| Recipe | Run | What it showed |
|---|---|---|
| `Q520Boulder` | none (the folder was missing) | `-LEVEL` loads a level that is not shipped |
| `Q520Corridors` | `F:\DK2Evidence\studies\q520-corridors--2026-11-01--001\payload` | Each wall turn costs a tenth of the boulder's health. Creature hits are half the creature's level-scaled maximum, at Secret4's 100%. A boulder breaks at a wall once hits leave less than one turn. `docs/research/boulder.md` §9b |
| `Q520CorridorsGlobal` | `F:\DK2Evidence\studies\q520-corridors-global--2026-11-01--001\payload` | The same at campaign values (health 4000, 50%): a turn is +400, one hit kills, a boulder breaks on contact with a Dwarf of 3780 Hp or more. Two boulders destroy each other. A slap costs health, not wear. §9b |
| `M7Traps` | `F:\DK2Evidence\studies\m7traps--2026-11-01--001\payload` | Boulder against doors, Sentries, water and lava (`boulder.md` §9b). Goblins flee a Hero Sentry, which fires at 6 tiles (`traps.md` §14). The Jack reaches the heart corner ours cannot reach, and its explosion hit the Heart (`jack-in-the-box.md` §7). The keeper's Imp walked onto the Jack trap unprompted to claim its tile |
