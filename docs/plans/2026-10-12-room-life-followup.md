# Room life follow-up

Implement the three owner requests in the existing classic worktree, verify
the combined result, then merge to master (authorized 2026-10-12).

1. Construction: implement the original queued player build waves, with
   engine-neutral effect requests and original-resource playback through the
   asset provider. Keep the 50ms visual clock separate from simulation time.
   Cover single tiles, dragged rectangles, queueing, refusal and cleanup.
2. Prison rats: research the original producer and object states, then add
   spawning and wandering through existing room/object systems. Test timing,
   room boundaries, deterministic draws and snapshot/hash coverage.
3. Portal arrival: retain state193/action49's existing timing and select the
   Entrance animation during arrival. Test start, hold, completion and fallback.
4. Review each implementation independently. Run the full Release suite with
   game data, campaign replay checks, Unity sync/compile and documentation/KB
   checks. Update the paired hash/save versions for simulation changes.
5. Commit only intended changes. Preserve both worktrees' local editor state
   and the master's local handoff edit while merging and checking the result.

Simulation owns game behavior; presentation owns resource playback. Research
facts remain in their canonical research documents and function records.
