# Restore native Heart routes without reopening the central core

Scope: revert ADR-0452's square mask and square movement guard, retaining
ADR-0439's earlier small core guard. Owner authorized reverting a harmful
ADR-0452 on 2026-10-17. Native route and frozen-world movement evidence live
in `docs/research/imp-boundary-route.md`.

1. Add a failing test for the first native movement segment the square rejects
   and an install-gated test for the captured Level4 route.
2. Restore raw navigation-byte routing and the earlier circular core guard;
   replace platform-only expectations with core protection, outward escape,
   target projection and ruined-Heart coverage.
3. Repeat the captured-body integration control with the candidate guard;
   require all native positions and velocities through arrival to match.
4. Advance hash/save version to 269 (266–268 belong to preserved candidates),
   update the determinism pin and decision/evidence records.
5. Run focused and simulation checks serially, review the candidate, commit
   only named files and await merge approval. Keep ignored captures.
