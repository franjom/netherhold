# Library capacity implementation plan

Goal: mirror the Library arm of the capacity test; evidence lives in
`../research/library-capacity-draws.md`.

- [x] Add regression tests for repeated finder draws, full numeric capacity,
  shelf refusal, entry claims and non-Library checks; run them red.
- [x] Carry the shared RNG in execution hooks, preserving existing constructors.
  Add an execution capacity entry point; leave numeric queries pure. Detached
  hooks check shelf availability without a random stream, as setup operations.
- [x] Wire Library finders, claims, claim fallback and FindRoomWithPlace.
  Keep the job-loss diagnostic query pure; it must not repeat native draws.
- [x] Advance hash/save436, measure the pin, run targeted/full/Unity checks and
  paired native control using the existing declared1400 inputs.
- [x] Update canonical evidence, ADR0704 and affected function records.
  Run reviewer, resolve objections, then documenter and gates.
- [x] Freeze add-only external evidence, commit and merge locally; preserve
  the owner's Unity scene and perform no cleanup.
