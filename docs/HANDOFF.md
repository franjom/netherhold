# Handoff

Current state and the next useful action live here. Git holds the history. DK2
facts and their evidence live in docs/research/, decisions in docs/DECISIONS.md,
and unknowns in docs/research/open-questions.md (AGENTS.md: one fact, one home).

## Current state

The milestone table and acceptance criteria are in DK2_REIMPLEMENTATION_PLAN.md
§8. Regenerate its numeric status with dk2tools status after a Release test run;
do not maintain counts here. For exact build coverage use dk2tools coverage
--all and dk2tools coverage states.

- M0–M4 are closed. M5 has an original-game idle-trigger comparison on two
  levels (docs/experiments/). M6 remains partial: original packet replays run,
  with the remaining timing differences in Q444 and navigation-captures.md. The
  seed spread of the Level 1–3 replays against DK2's own seeds (2026-10-06) is in
  experiments/2026-10-06-packet-replay-seeds/; the open difference is Q567.
- M7 remains partial: combat, hero parties, door contact and the door fight
  (ADR-0318), hero gates (ADR-0320), every shipped trap (ADR-0321, ADR-0331) with
  the Boulder's rolling object (ADR-0333), the Jack In The Box's Jack (ADR-0337) and
  the reveal (ADR-0338), the Guard Post (ADR-0330), trap upkeep and damage (ADR-0326)
  and creatures fighting a revealed trap (scan D, ADR-0340) run; disarming is deferred under
  ADR-0329's scoped inference (Q455); shot heights go to M9 and the Boulder's slap to object slapping
  (ADR-0341). Left: the owner's observations in experiments/doors-and-traps.md — Q520's wall is
  disputed, Q513, Q524 and Q362 unwatched (2026-10-03).
- Keeper spells (ADR-0350, accepted 2026-10-04): Create Gold, Tremor, Turncoat,
  Summon Horny, OneShotReaper and Possession's simulation side are built, and Sight
  of Evil casts with an empty effect (ADR-0352), its region presentation (Q194, Q534). Research
  grants every upgrade through ResearchTreeRules (KeeperSpellUpgradeTests); the
  upgraded effects are the bonus data and Possession's 70 % upkeep. Left unbuilt:
  Horny's idle AI, the possessed action arms in Q535/Q536, the third cast path
  (action 56 only, Q537) and the held-creature regeneration in Q152.
  Evidence: research/keeper-spell-*.md.
- M12 remains partial. DK2.AI.KeeperAI is an input source that emits
  PlayerCommands through a detached, serialisable observation (schema 19).
  The proactive task machine (tasks 0–7) digs, expands, builds rooms, manages
  gold and imps, and crosses water or lava with bridges. Every message type its
  walk reads is produced where ours has the event and consumed (ADR-0353): room
  messages 41–44 and 32–35 make it build, expand or re-choose a room (ADR-0327,
  ADR-0335), 17/18 make it pay, feed, move or dispose of a creature, and 12, 34
  and 47 start a threat response; the unproduced rest is Q542–Q544. Reactive combat
  messages, threat response, wounded retreat, the battle step, Call to Arms,
  Thunderbolt, Heal, Chicken and Inferno run through simulation commands, and the
  gold step's Create Gold and step 14's Sight of Evil are cast where DK2's are (ADR-0352).
  The attack planner selects a target, tunnels, stages and calls its army;
  step 9 sends creatures to Training or a Combat Pit. See KEEPER_AI.md §12.8
  and the canonical computer-keeper research notes for exact rules.
- Unaided modern scenarios show Harkan moving creatures onto its Training
  Room's floor on Level 17, Nemesis preparing an attack on Level 18, and
  Drako bridging Level 11A's lava and calling its
  army toward Morgana, and Harkan on Level 17 growing its Treasury after its
  imps find no store (Q518). Two idle captures of DK2's own keepers on the same
  maps (Level 17, 2026-10-05; Level 11A, 2026-10-06) are compared in
  computer-keeper-observations.md, whose last section is the M12 acceptance
  table: every verb agrees in kind; the open timing differences are Q509, Q555
  and Q563 (Q497 and Q509 are answered).
- The creature state machine dispatches category hooks. Combat enter and
  leave are partially built (ADR-0319, combat-category-hooks.md);
  their unmodelled words and the update hook are listed there. The Casino,
  Torture and Manufacture leave hooks and the stored maker count are built
  (ADR-0322, leave-hook-bodies.md); the Sleeping leave is believed presentation only.
  Consult CategoryHooks.Slots for the current status of every hook.

## Next work

1. M12: the message producers left unbuilt wait on unread fields or unmodelled
   state (Q542 rescue, Q543 finder gates, Q544 Graveyard; computer-keeper-messages.md
   §4). Verify each in DKII.EXE, then test the command's
   effect through the simulation. The research move, prisoner/special
   decisions, trap/door decisions and personality loader also remain
   (KEEPER_AI.md §12.7). Do not infer an UNKNOWN rule from a name.
2. M12: the same-map comparison is done (two captures, the acceptance table at
   the end of computer-keeper-observations.md). The owner decides whether the
   reported differences (Q509's re-lift delay, Q555's Prison timing, Q563's
   defence and call timing) close M12 or are narrowed first. Compare with every
   computer keeper running and on the record's gold, as DK2 runs the level.
3. M6 and M7: use the plan's run order and the open-question rows for the
   remaining replay, combat, hero, trap and door work. Avoid reproducing their
   detailed backlogs here.
4. Goal 7's writer/editor API is complete and independently verified across all
   five milestones and the whole branch; all review findings are resolved.
   ADR-0346 and `src/DK2.Data/README.md` describe the surface and format limits.
   The editor UI and playability validation remain future work.

After faithful M12, keep it as selectable Classic AI; explore a separate
Enhanced keeper using the same observation/command interface (KEEPER_AI.md
§12.7). Enhanced behavior is outside M12's fidelity target.

## Where to work

- src/DK2.Simulation owns game rules and mutable state; it is independent of
  DK2.AI and Unity. src/DK2.AI observes and emits commands. src/DK2.Harness
  runs headless scenarios; tests/DK2.AI.Tests and
  tests/DK2.Behavior.Tests cover keeper decisions and stock maps.
- docs/ARCHITECTURE.md gives the project boundaries and system order.
  docs/DECISIONS.md records design choices. docs/research/functions/ is the
  canonical knowledge base for each DKII.EXE function.
- The reference install is E:\GogGames\Dungeon Keeper 2. DK2_DIR overrides
  discovery; an invalid override means no install. Tests marked InstallFact
  or InstallTheory skip without game data; DK2_REQUIRE_INSTALL=1 makes a
  missing install fail (ADR-0100).

## Run and verify

    dotnet build DK2.slnx -c Release
    dotnet test DK2.slnx -c Release --no-build
    dotnet build unity/DK2.Unity.Compile/DK2.Unity.Compile.csproj -c Release
    dotnet run --project src/DK2.Tools -c Release --no-build -- verification-ledger --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- kb check
    dotnet run --project src/DK2.Tools -c Release --no-build -- coverage --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- docs --check
    dotnet run --project src/DK2.Tools -c Release --no-build -- status --check

Run dk2tools status after the tests when the plan's generated count changes.
Run dk2tools kb index after changing function records; never edit INDEX.md by
hand. Check the changed commit with git diff --check. A simulation state
change requires matching GameSimulation.HashVersion and
WorldSnapshot.FormatVersion, a new determinism pin, and a hash-versions.md
row. An observation-format change requires a schema bump.

The Unity compile project is outside DK2.slnx. Run
unity/sync-assemblies.ps1 after a change under src/ before an editor smoke
test. Scenario format is in scenarios/README.md; harness output goes under
build/scenario-runs/ and is ignored.

## Research and workspace notes

- Follow AGENTS.md's source order: DKII.EXE through Ghidra first for a
  behavior question, then shipped data and original-game experiments.
  The ghidra-queries skill and docs/RESEARCH-QUEUE.md describe Ghidra
  12.1.4/PyGhidra. The runner clones the read-only D:\Ghidra\DK2_RE
  project per run. Keep decompiler output under reverse-engineering/ or
  scratch, never in tracked files.
- Read a function's record before querying it. Put each new finding or
  correction in that record with a dated History line. Write behavioral
  evidence once in its research note; link from other documents. Distinguish
  CONFIRMED, STRONGLY INFERRED, HYPOTHESIS and UNKNOWN.
- An original capture's turn is one tick behind the game's turn
  (ADR-0119); timer stamps need no correction. The live-memory and save
  tools are indexed in reverse-engineering/README.md and
  navigation-captures.md.
- Check git status and git worktree list before editing: other sessions may
  be working concurrently. Stage named paths, not git add -A; keep original
  game files and Ghidra output out of commits. If Git reports dubious
  ownership in this environment, use a per-command safe.directory setting.
- Documents and source have budgets enforced by dk2tools docs; AGENTS.md
  has the limits. Replace stale claims rather than adding dated amendments.
  PowerShell is available here; pwsh is not. If a local shell rewrites a
  UTF-8 document, preserve its existing BOM and line endings.
