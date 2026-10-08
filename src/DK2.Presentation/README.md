# DK2.Presentation

Everything between the simulation and a renderer that does not need an engine.
`WorldView` projects a `WorldState` for one keeper; `ScenePresenter.Present` diffs it
against what was last shown and drives an `IRenderTarget` (create, update, remove,
`Flush`) with assets from an `IAssetProvider`. `TextRenderer` is the same job against
a character grid. `PlayerInputTranslator` turns a `PlayerIntent` into commands,
`TilePicker` picks a tile from a ray, and `TorchLayout` and `RoomWalls` compute
placement.

Targets `netstandard2.1`. Depends on `DK2.Core`, `DK2.Data.Model`, `DK2.Simulation`
and `DK2.Assets`. The Unity adapter (`unity/README.md`) implements `IRenderTarget`.

Test: `dotnet test tests/DK2.Presentation.Tests`.
