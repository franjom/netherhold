# Performance acceptance

Owner requirement, revised 2026-10-25: minimum 60 FPS during gameplay on the
current laptop at 1920x1080 in a standalone build. Hardware, resolution and required
scenario coverage are recorded in
[`performance-target.json`](../tools/classic-unity-probe/performance-target.json).
The resulting frame budget is 1000/60 ms (about 16.67 ms). An average or percentile below it does
not excuse a slower frame.
The optimization goal remains 100+ FPS; meeting the 60 FPS floor does not end
performance work.
Desktop backend selection follows [ADR-0540](DECISIONS.md#adr-0540--prefer-vulkan-on-supported-desktop-platforms).
Selecting a backend does not certify the frame budget.

## Measurement and acceptance

- Capture real screen rendering in a release player, with the ordinary
  simulation, camera, lighting, effects and UI. Development captures diagnose
  markers; they cannot certify the release target.
- Include the first use of effects and assets. Level loading is measured
  separately; do not relabel a gameplay hitch as warmup. The arrival fixture
  freezes before introduction, not during it.
- Preserve CSV, CPU/GPU/API metadata, source and built-DLL SHA256 fingerprints,
  logs and a nonblank rendering proof. Screenshot encoding belongs outside
  the timed interval. Skipped rendering or incomplete presentation evidence
  invalidates acceptance.
- Record maximum frame interval, slow-frame count, longest slow-frame sequence,
  and p99. Use separate GPU diagnostics to investigate duration and waits;
  unavailable GPU data is never a zero-cost claim. Run comparisons serially and repeat
  release acceptance runs on the target machine. Report machine interference
  and failed runs; do not quietly remove slow samples.
- Verify visual ordering, lighting, blending, animation and effect content
  independently. A faster image with missing effects is a failed change.

The local command is documented in
[the probe README](../tools/classic-unity-probe/README.md#portal-arrival-performance-research).
`frame_budget.py` defaults to lightweight FPS qualification: Unity Frame Timing
Stats and profiler recorders are disabled, and PresentMon captures presentation
intervals without GPU tracking. Every CPU loop and native presentation interval
must fit the budget. This checks application cadence, not physical display refresh.
GPU diagnostic mode cannot certify FPS acceptance. The checker returns 0 only
for a valid passing FPS capture, 1 for a budget failure, 2 for invalid evidence,
and 3 for a valid diagnostic capture.
Its regression tests run in CI without a GPU. CI does not run the actual
graphics benchmark; a green CI build is not a performance acceptance result.

## Coverage and current status

The implemented acceptance fixture is Level2's Warlock introduction. Other
entries in the target's required scenario list still need rendered acceptance
fixtures and results. A passing capture certifies only that capture, not the
whole game. New effects, renderers, shaders and gameplay workloads need
representative regression coverage before claiming the target is met.

The target is currently unmet. Arrival measurements and optimization evidence
have one home: [portal arrival performance](research/portal-arrival-performance.md).
