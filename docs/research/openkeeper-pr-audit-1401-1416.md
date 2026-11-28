---
systems: [tooling-process, creatures, computer-keeper, audio, hud-front-end, navigation]
---
# OpenKeeper PR relevance from #1401 to #1416

Reviewed 2026-11-28, continuing the [previous audit](openkeeper-pr-audit-1366-1400.md).
GitHub listed 15 PRs in this range, ten merged and five open. #1407 was absent
from the PR listing. The review compared the upstream changes with this checkout
and used a fresh binary check for #1405 and the mood gate in #1408.

| PR | Disposition |
|---|---|
| [#1401](https://github.com/tonihele/OpenKeeper/pull/1401) | The startup synchronization fix targets OpenKeeper's app-state lifecycle. |
| [#1402](https://github.com/tonihele/OpenKeeper/pull/1402) | PortalRules and PortalTests cover a zero-size Lair attraction with no Lair. |
| [#1403](https://github.com/tonihele/OpenKeeper/pull/1403) | WalkOverGoldSystem covers the separate worker and other-creature pickup paths. |
| [#1404](https://github.com/tonihele/OpenKeeper/pull/1404) | TravelArrivalSystem and the imp movement handlers cover the handover on arrival. |
| [#1405](https://github.com/tonihele/OpenKeeper/pull/1405) | Both Call to Arms gates built at hash version 538. See [need anger](functions/00478D00.md) and [payday](functions/004BE1A0.md). |
| [#1406](https://github.com/tonihele/OpenKeeper/pull/1406) | WorldHoverText uses string 348 for unexplored tiles with fog enabled. The PR suggests checking that the border ring reaches that branch in the Godot view. |
| [#1408](https://github.com/tonihele/OpenKeeper/pull/1408) | CreatureJobSystem's mood prelude checks the need-anger state flag and Call to Arms. The latter branch was checked against the [creature tick](functions/00476FC0.md). |
| [#1409](https://github.com/tonihele/OpenKeeper/pull/1409) | OpenKeeper reachability cache. Performance lead if profiling shows repeated route-existence checks are costly here. |
| [#1410](https://github.com/tonihele/OpenKeeper/pull/1410) | Query posts the GUI button command. Verify whether playback uses the dedicated info category. |
| [#1411](https://github.com/tonihele/OpenKeeper/pull/1411) | The bottom panel posts a resize command. Check playback on explicit restore. |
| [#1412](https://github.com/tonihele/OpenKeeper/pull/1412) | Check once-per-press playback for both minimap buttons, including a drag. |
| [#1413](https://github.com/tonihele/OpenKeeper/pull/1413) | PlayerMessages, KeeperThreatResponder, KeeperRoomRequests and KeeperMoodRemedies cover the inbox behavior. |
| [#1414](https://github.com/tonihele/OpenKeeper/pull/1414) | FearRules and CombatFearTests cover withdrawal to the lair, heart or an owned room. |
| [#1415](https://github.com/tonihele/OpenKeeper/pull/1415) | ExploreRules and ExploreTests cover the Explore job, including local hops after arrival. |
| [#1416](https://github.com/tonihele/OpenKeeper/pull/1416) | KeeperThreatResponder debits threat without moving the fighter while the payload tile remains owned, and seeks a replacement when it has been lost. |
