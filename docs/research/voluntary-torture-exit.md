---
systems: [creatures, rooms]
---
# How a voluntary torturee gets off

No self-satisfaction exit was found. The paths below are STRONGLY INFERRED from v1.70 binary reads; **whether an undisturbed self-strapped Mistress eventually leaves remains UNKNOWN**. The owner remembers this happening; this pass neither reproduces it nor proves it impossible.

## Verified exits and forced-drop comparison

- **Call to Arms:** [004BDDA0](functions/004BDDA0.md), bit 14 tests at 0x004BDED7 and 0x004BDEFC, attempts kick 135 at 0x004BDF0E, then routes toward 190. The voluntary flag bypasses the held-creature exclusion; a hand-dropped creature lacks it.
- **Heart defence:** [004D9690](functions/004D9690.md), successful route probe at 0x004D99C9, bit 14 at 0x004D99D2, attempts kick 135 at 0x004D99E4, then route 254 at 0x004D9A1A. This is an external alarm, not satisfaction. Remake implementation: [combat-alarm.md §7](combat-alarm.md#7-the-hearts-dispatch-0x004d9690-2026-10-09).
- **Cleanup:** [0047B4A0](functions/0047B4A0.md) frees the room when category 15 is left; bit 14 preserves the dropped-job field before being cleared. [00484CC0](functions/00484CC0.md) resets attachment angles and returns 0, allowing idle if the subsequent move failed.
- **Device loss:** [00484590](functions/00484590.md) attempts 136 at 0x00484927 when room/device validation fails. This is shared with forced torture; 136 may find another device rather than leave permanently.

## Paths checked


- **Job entry:** job 7 dispatches through [00478800](functions/00478800.md) to [00483AE0](functions/00483AE0.md), which routes to a chamber resuming 127. [00483B90](functions/00483B90.md) then routes a free-device user to 130 and sets voluntary bit 14. There is no distinct satisfaction-session state on this route.
- **Job timing:** [004780E0](functions/004780E0.md)'s five-second timestamp gates selection attempts. Neither that body nor the dispatcher or torture entry installs an active-session timeout. The Mistress's shipped table was not newly extracted in this pass; this establishes the job 7 route, not her table probabilities.
- **Mood:** [00478B80](functions/00478B80.md) passes selector 8 as notification data. Negative bucket 5 adjustments cannot fire its positive-worsening notifications or trigger 11. [00487030](functions/00487030.md) clamps and recomputes mood without a state transition; there is no pleased-event release in these bodies.
- **Hunger:** [00477C20](functions/00477C20.md) contains no starvation-health arm or threshold override of the category and busy guards. Its health drain is for sleep debt. This does not substitute for an exhaustive scan of all hunger-field readers outside that routine. The earlier data-only starvation negative is in [creature-needs.md](creature-needs.md#hunger-costs-no-health-on-purpose--strongly-inferred-data-by-absence).
- **Outer update:** [00488930](functions/00488930.md) was decompiled and its health-modifier neighborhoods inspected. The 32-bit +0x3EA accesses at 0x00488C81, 0x0048A011 and 0x0048A17D operate on bits 21, 1, 17, respectively; they do not test voluntary bit 14. No hunger or full-health release was identified in this bounded read. Callees were not all traversed.

- **Strap and duration:** [00484110](functions/00484110.md) takes successful voluntary admission to 131, the same handler used by forced entry. [0047B590](functions/0047B590.md) resets duration after its threshold but only enemies take the death/conversion branch. Own-room victims remain; the hook always returns 1. [00476FC0](functions/00476FC0.md) has no additional session timer around the hook/handler.
- **Room/device/action:** tile-change callbacks [004F1F10](functions/004F1F10.md) and [004F1FB0](functions/004F1FB0.md) send holders to 136 irrespective of voluntary status. The actual room tick [004F1D60](functions/004F1D60.md) runs furniture-effect timers, with downstream effects not all audited. [004F2180](functions/004F2180.md) only finds devices. [00499300](functions/00499300.md)'s chair randomness changes presentation; [004A1C60](functions/004A1C60.md) maintains the wheel transform.
- **Player/combat:** [0048B8A0](functions/0048B8A0.md)'s own-room torture slap deals damage and returns. Possession entry [0048DEA0](functions/0048DEA0.md) retains state; its exit arm attempts idle. Eligibility and refusal still require tracing. Pickup preparation [004BFBD0](functions/004BFBD0.md) attempts 75 with an idle retry; permission [0048E6A0](functions/0048E6A0.md) has no voluntary-bit distinction. These are interventions, not hovering. [0045A8B0](functions/0045A8B0.md) lets voluntary victims remain attackable; that flag update alone does not establish combat releasing them.
- **Flag census:** InsGrep over disassembled instructions for offsets 3EA/3EB/3E9 returned 190 substring hits, including unrelated constants. No explicit creature-byte 3EB reader appeared. The known high-byte bit 40 consumers above were rechecked, along with the outer-update masks. This is not an exhaustive data-flow proof: register copies, aliases and undisassembled code remain limitations. No claim that all 32-bit read/modify/write consumers were followed is made.

## Native test to close the question

Use v1.70 with one owned Mistress, an accessible Hatchery and Lair, enough gold to pay, and an owned Torture Chamber containing a single wheel. Remove enemies, Call to Arms and scripts that redirect creatures. Let her choose the device herself; do not drop, slap, possess or pick her up. Start timing when visibly strapped. Observe for 20 minutes of unpaused normal-speed play, recording hunger, health, mood and any departure/destination. Repeat with a chair, then run matched Hand-drop controls. Twenty minutes is an observation window, not a binary-derived timeout; a non-exit does not prove she never leaves.

If she leaves, retain a save before the event and record whether an intruder approached the Heart or the device/room changed. A debugger trace of state setters for that creature, recording old/new state and return address, distinguishes 135, 136, combat and direct idle. Store any captures or logs as new files under F:/DK2Evidence, never only in a checkout. Separately test Call to Arms as a positive control: voluntary release is expected from the binary; forced entry should retain the held-creature exclusion.
