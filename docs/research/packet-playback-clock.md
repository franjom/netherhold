# Packet playback clock

## Binary and native proof

CONFIRMED: playback consumes records sequentially. Loader 0055E7D0 reads the
68-byte file header. Reader 0055EA10 reads 17 count/header bytes, count*18 action
bytes and 12 trailer bytes, without gating on the stored timestamp. Session
00526752 reads before world call 00526AA7; increment 00526B9E follows the world
return. Initialization 00526254 sets the first executed clock to 1.
Producer 00522790 stores parent clock+1 at packet+1 (005227C3-005227CA),
which explains why ordinary recorded stamps lead the playback clock by one.

An independent reader consumes the complete Level 5 file: 6415 records, contiguous
stored stamps 2..6416, zero residue. In three atomic full images, the native
input at entry ESP+0x78 matches exactly one complete file header/action block.
The world clock, return address 526AAA and return ESP=entry ESP+8 are guarded.

| Completed native turn | Record ordinal | Stored stamp | Actions | Header byte offset |
|---:|---:|---:|---:|---:|
| 1400 | 1400 | 1401 | 0 | 42818 |
| 1615 | 1615 | 1616 | 1 | 51771 |
| 1900 | 1900 | 1901 | 0 | 61908 |

Evidence: `F:/DK2Evidence/studies/q712-packet-playback-clock--2026-11-02--001/`,
including the exclusive-write producer, input hashes and complete-block hashes.
The file stamp is retained evidence, not the native scheduling clock. Fixed
minus 1 is only equivalent for this contiguous file starting at stamp 2.

## Pre-edit mismatch inventory

| Binary address | Existing remake behavior at hash 370 |
|---|---|
| 0055EA10 / 00526752 | PacketLog.Read keys actions by the stored stamp; ReplayCommands.At(turn) executes them one turn late for this file. Native consumes the first body record on turn 1. |
| 0055EA10 | The remake infers action count from the next stamped header. Native reads the preceding count byte and can consume the final complete record without a following header. |

Before a fix, define playback origin for explicit byte offsets, retain raw
stamps for provenance, and keep entity references/resolution on one clock.
Entity-map and partial-world restoration are not proved correct by these three
packet-buffer matches. No new outcome measurement is claimed here.
