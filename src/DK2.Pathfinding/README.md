# DK2.Pathfinding

Routing for the simulation. `IPathGrid` and `IPathfinder` describe a tile grid;
`GridPathfinder` is the grid implementation. `NavMesh` is the navigation mesh, with
`MeshRouter` (`TryRoute`, `TrySearch`), `Funnel` and `GateWalk` for corridor search,
string-pulling and gate walking.

Targets `netstandard2.1`. Depends on `DK2.Core` only. `DK2.Simulation` supplies the
grid that reads the live world (`WorldPathGrid`) and owns every route decision.

Test: there is no `DK2.Pathfinding.Tests` project. The router is exercised by
`dotnet test tests/DK2.Simulation.Tests` (for example `MeshRouterTests`,
`GateWalkTests`) and `tests/DK2.AI.Tests`.
