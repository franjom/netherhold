# DK2.AI

The computer keeper. `KeeperAI` is built from a player's `KeeperAIParameters` and a
random source; each call to `Think(GameDatabase, WorldState)` reads the world through a
`KeeperView` snapshot and returns the `PlayerCommand`s that keeper issues. It keeps its
own `KeeperAIState`. Its pieces (planning, threat response, room planning, training,
battle and retreat steps, route cache) are the other `Keeper*` types. What each one
does, and how sure we are of it, is in `docs/KEEPER_AI.md`.

Targets `netstandard2.1`. Depends on `DK2.Core`, `DK2.Data.Model` and `DK2.Simulation`.
It sends the simulation commands only, never touching state.

Test: `dotnet test tests/DK2.AI.Tests`. `DK2.Harness` runs it on shipped levels
through `computer_keepers` (see `scenarios/README.md`).
