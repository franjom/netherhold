# Q712 party arrival implementation plan

Goal: reproduce the fully read non-gate party arrival branch, without fitting
its timing or combat outcomes to scores. Spec: user's current task and
[binary comparison](../research/party-arrival-lifecycle.md).

Bounded design: HeroPartyRules selects269 from original spawn terrain except
33/37. A PartyArrivalRules handler owns its age8 transition and vertical state;
TremorCreatureSystem dispatches either269 or the existing recovery handler once
per creature update. Existing state flags gate combat. Reuse the shared draw
bit/age representations; save/hash the pending height step. Keep direct trigger
creation and portal paths separate. Read-only research precedes implementation.

- [x] Read binary handlers, table flags and null category hooks; read spawn paths.
- [x] Record native439 lifecycle and list source mismatches before changing code.
- [x] Write address-cited tests and observe failures against unmodified sources.
- [x] Add party arrival and missing recovery action cancellation.
- [x] Bump hash/save together, update pin and hash history after checking master.
- [x] Prove regression failures with old source in a scratch copy.
- [x] Run focused and full appropriate checks with processor count2/-m:2.
- [x] Freeze verified build outside checkout with SHA provenance.
- [x] New add-only L5 seeds1-10 and L4 resync studies; score and compare362.
- [x] Re-measure exposure and254 exits, including native439 timing control.
- [x] Update canonical research, KB History, ADR, questions and handoff.
- [x] Actual reviewer, fix objections, actual documenter, inspect edits and gates.
- [x] Recheck master allocations; stage named paths, commit with required trailer.

Files: new PartyArrivalRules and lifecycle tests; HeroPartyRules caller;
TremorCreatureSystem dispatch and TremorRules starter; movement's vertical
subset; CreatureState/ActivityRules and guard projection; snapshot/version/pin;
canonical research/KB and generated index. Existing scoring/replay scripts are
reused with new evidence run directories. No sweep, merge, push or cleanup.
