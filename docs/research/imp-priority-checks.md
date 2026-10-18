# Executable checks of Imp job precedence

2026-10-18, current-main hash/save 274. Research only; no gameplay change.
The preceding measurement and unresolved upstream boundary remain in
[imp-hatchery-work.md](imp-hatchery-work.md) and Q612. Matching one claim
choice under a tile-input control does not validate the whole priority chain.

## Experiment and scope

Re-read [state 10](functions/00463DF0.md) whole. Execute its original bytes
with controlled per-kind search success, ring-scan success, turn, random and
state-setter dependencies. Log every attempted search kind. Read the shipped
table at `0x00698658` and the nine-dword records at `0x006986A0`: the ordinary
search list and effective retry flags agree with the existing reading.
This exercises the chooser's branch order, not native task eligibility,
route finding, stealing or an actual captured world.

Independently run the modern real handlers on a synthetic shipped-data fixture:
one worker, reachable claimable floor, a marked dig and reinforceable walls.
Only the last-kind/aim pair changes for the first six rows. No original-world
state equivalence is claimed. Results are **CONFIRMED computations**.

| Situation | Native controlled precedence | Modern real-handler result |
|---|---|---|
| No prior kind; dig, claim and wall available | dig search before claim/fallback | Dig |
| Last kind claim, no aim | claim retry before ordinary searches | Claim |
| Last kind dig, no aim | excavation retry first | Dig |
| Last/aim both reinforce | reinforce retry before digging | Reinforce |
| Last reinforce, no aim | ordinary searches reach digging first | Dig |
| Last/aim both claim | aimed claim retry first | Claim |
| Aimed claim unavailable, digging available | two failed claim searches, clear aim, ordinary dig | Dig; aim cleared |
| No marked dig, claim available | ordinary claim before fallback | Claim |
| Dig and claim unavailable, wall available | reaches Tier B | Reinforce |

Additional original controls confirm a failed aimed reinforcement retry clears
the aim before ordinary digging; loose-object search precedes ordinary digging;
and a previous miner tries excavation, then depositing, before ordinary objects.
These three controls have no corresponding modern fixture in this experiment.

The wrapper for [SearchKind](functions/00468520.md) masks its kind to a byte,
as the real entry does at `0x00468527`; upper argument bits can contain unrelated
stack data. A wrapper reading the entire dword gives invalid results.

## Remaining differences and interpretation

Fresh read and five controlled executions of the original
[Tier-B tile chain](functions/00469580.md) confirm its four handler steps.
`TrySweep` in `ImpTaskSystem.Selection.cs:271` implements only the first two;
the later object and marked-Secret-Door steps remain absent. The known Secret
Door gap is documented in [its handler record](functions/0046CB80.md).
Ordinary object search earlier in the chooser often covers the object case;
this experiment does not prove its later fallback is redundant or impactful.
Neither omission is established as the cause of the measured Hatchery gap.

No reversed core precedence is found in the nine tested modern cases. This is
not a proof that all priorities, interruption rules or eligibility predicates
match DK2. Do not infer full equivalence from these fixtures or tune a priority
to compensate for a different work history. Continue Q611 at an actual update
boundary; separately preserve the already identified fallback omissions.

The canonical priority summary previously omitted the retry/drop preconditions,
described the ineffective kind-14 search as operative, and retained the obsolete
Secret Door unknown. Those descriptions are replaced in imp-task-priority.md;
the game's rules are unchanged.

## Reproduction

Ignored directory in the retained worktree:
`build/imp-acceptance/reverse-engineering/imp-priority-contract-20261018/`.
`native_matrix.py` executes 12 state-10 and five Tier-B controls under Unicorn,
with fixed code stubs reading variable availability data. `Program.cs` and
`Probe.csproj` run nine modern real-handler fixtures. JSON/text outcomes and
`verify.py` preserve the assertions. No campaign replay, full test suite or
profiler was run for this research; no campaign improvement is claimed.
