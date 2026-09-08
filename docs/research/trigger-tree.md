# World trigger-tree traversal in DKII.EXE v1.70

**Scope:** the world tree walker at `0x00518150`, its record initialisers at
`0x00508600` and `0x00508810`, and the shipped `*Triggers.kld` corpus. This
describes traversal and repeat retirement; it does not establish action-type
semantics or add an executor to the reimplementation.

## Confidence

| Claim | Confidence | Basis |
|---|---|---|
| Positive ids select tag 213 conditions; negative signed ids select tag 214 actions; zero ends a chain | **Strongly inferred** | Direct instruction read of `0x00518150` |
| `NextTriggerId` is unconditional sibling continuation | **Strongly inferred** | Every active, inactive, true and false path rejoins the load from record `+0x0A` |
| `ChildTriggerId` is visited only after the current condition or action succeeds | **Strongly inferred** | Calls from `0x005181C6` and `0x00518242`, after each dispatcher succeeds |
| A condition's `RepeatCount` is passed to its child chain; value 1 retires each successful action | **Strongly inferred** | Byte `+0x0F` is the recursive argument; the action path clears its active bit only when that byte equals 1 |
| Across all 9,161 shipped records, every non-zero child and next link resolves within its file | **Known** | Exhaustive read of 66 shipped trigger files |

`Confirmed` is reserved for an observation of the running game. These control-
flow claims come from the binary.

## The walk

The caller supplies the root id from world offset `+0x7310` and a repeat byte
from `+0x731A`. Q228's cadence result is kept in `trigger-state.md`: the caller
runs once per game turn.

Ids are treated as signed 16-bit values. A positive id is looked up in the
condition array at `+0x7308`; a negative id is looked up in the action array at
`+0x730C`. Action ids therefore appear as large unsigned values in the file
model. Zero returns without evaluating a record.

For each resolved record:

1. If its runtime active bit (`+0x10`, bit 0) is clear, skip evaluation.
2. Otherwise run the appropriate world dispatcher.
3. On success, recursively visit `ChildTriggerId` (`+0x0C`). A condition passes
   its own `RepeatCount`; an action passes through the inherited value.
4. Visit `NextTriggerId` (`+0x0A`) whether the record was inactive, failed, or
   succeeded.

The result is a sibling chain whose successful nodes may enter child chains.
A nested condition replaces the inherited repeat policy for its own children;
the condition's next sibling remains under the policy of the current call.

The lookup code uses the first record if a non-zero id is absent. This is a
binary-visible fallback, but it is not exercised by shipped data: every one of
the 9,161 records' non-zero `NextTriggerId` and `ChildTriggerId` values resolve
within the same trigger file.

## Repeat and active state

The condition initialiser sets the active bit exactly when the authored
`RepeatCount` is non-zero. The action initialiser sets actions active regardless
of their own authored repeat byte.

When an action succeeds, inherited repeat value 1 clears that action's active
bit before its child is visited. Other inherited values do not retire the
action in this walker. The action record's own `RepeatCount` is not consulted.
Across the shipped corpus, condition repeat values are:

```text
0: 23   1: 3484   2: 5   10: 4   30: 5   255: 232
```

The condition path contains a call to a helper that decrements the condition's
repeat byte and clears its active bit at zero. That call requires the recursive
walk to return non-zero, but every exit from this walker returns zero. The same
shape occurs in the player, creature and area walkers. Therefore condition
repeat decrement is unreachable here: values other than zero control initial
activation, and value 1 additionally makes successful descendant actions
one-shot.

This dead return-dependent path is preserved as a binary quirk, not treated as
an intended countdown. A running-game experiment could still check whether
some external enable/disable path mutates the same records.

## Implementation boundary

`TriggerConditionEvaluator` currently evaluates only one supported condition.
There is no tree executor or action dispatcher in the reimplementation. An
executor can now reproduce link order and retirement without guessing, but its
action effects still require their own binary reads.
