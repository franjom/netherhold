# Mechanic: VARIABLES(230) record layout — the global game-rule table

Source(s):
- ufdada/dk2-tools `variables_struct.bt` (payload layouts + 200 named ids)
- Direct parsing of the reference install's GlobalVariables.kwd and all
  per-level *Variables.kld files

Original version: DK2 v1.70 (GOG)

Evidence:
- Records are 16 bytes: s32 variableId + 12-byte payload.
  621 records x 16 == chunk contentSize of GlobalVariables.kwd, exactly.
- Payload shapes by id:
    1  CreaturePool             s32 creatureId, s32 value, s32 playerId
    2  Availability             u16 kind, u16 playerId, s32 typeId, s32 value
    43 Sacrifice                6 x u8 ingredient (type,id pairs), u8 rewardType,
                                u8 speechId, s32 rewardValue
    65 CreatureStats, 224 CreatureFirstPersonStats
                                s32 statId, s32 value, s32 level
    otherwise                   s32 value, s32 unk1, s32 unk2
- Values documented by earlier community work now CONFIRMED against real data:
  ClaimTileHealth=825, MineGoldHealth=-300, MaxGoldPerTreasuryTile=3000,
  DeadBodyDiesAfterSeconds=60, PayDayFrequencySeconds=600, ...
  Full table: docs/research/game-database-dump.md
- GlobalVariables.kwd also carries a THINGS(190) chunk (39 items) — the editor's
  default things? Unexplored (open question).

Observed behaviour: this file IS the game's balance specification. Per-level
*Variables.kld files override it (same record format).

Confidence: CONFIRMED (layout and values) / UNKNOWN (ids 17, 19, 29, 44, ...
present in data without names; semantics of unk1/unk2 payload fields)

Implementation: `DK2.Data.Kwd.KwdReader`, `DK2.Data.Kwd.KwdVariables`,
`DK2.Data.Model.VariableTypeId`

Tests: `DK2.Data.Tests.KwdReaderTests` (incl. full editor + 71 maps + 330 KLD corpus)

Known exceptions: LEVEL(220) chunks understate contentSize; reader attaches
trailing bytes to the chunk (validated look-ahead).

Open questions: unnamed ids; meaning of the THINGS chunk inside
GlobalVariables.kwd; CreaturePool lives in per-level files (global has 0).
