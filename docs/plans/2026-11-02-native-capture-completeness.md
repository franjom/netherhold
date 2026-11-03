# Complete native capture implementation plan

Goal: a normal capture retains every readable process-memory byte needed for
later binary queries, without an opt-in dump flag or a second native replay.
The owner requested this while the frozen Q712/hash372 resyncs were running.

Design: use the existing owned-process hardware debugger at World_UpdateTurn
entry/return. Capture standard decoded frames and all readable committed
regions at each selected completed turn. Store content-addressed compressed
blocks with frame indexes, so unchanged memory is retained without repeated
payloads. Record exclusions, registers, packet/source/executable hashes and
clock/seed/map validation. Only new files under F:/DK2Evidence are allowed.

The first requested turn and last turn also export existing oracle .mem.z
images; any other saved turn can be exported offline. No memory-category or
dump-enable flag exists. Seed setup happens at stopped turn1 entry, removing
the old polling/map-filter race. The packet supplies the level name; map
dimensions are discovered and then guarded, never silently defaulted.

- [x] Add old-API regressions for omitted mapped/read-only/large regions and exclusive writes; prove failure on archived source.
- [x] Implement complete region enumeration and add-only chunked oracle dumps.
- [x] Add tested compressed-block timeline, reconstruction, integrity and truncation checks.
- [x] Promote the atomic recorder into tools/oracle with complete capture as its only profile.
- [x] Preflight capture/readback at early turns, require all core records and active pools, preserve optional missing markers.
- [x] Run generated-PE tests, offline native-image controls and a short owned native smoke capture.
- [x] Actual reviewer/documenter and gates; keep frozen runtime unchanged.
- [x] Named-path capture-tool commit, excluding the separate runtime changes.

Limits: unreadable/guard/uncommitted addresses are enumerated and identified,
not invented. A failed read or incomplete frame is never marked successful.
Instrumentation pauses are measured; captures are not wall-clock performance
measurements and do not establish equivalence to an older replay trajectory.
