# DK2.Presentation

Everything between the simulation and a renderer that does not need an engine.
`WorldView` projects a `WorldState` for one keeper; `ScenePresenter.Present` diffs it
against what was last shown and drives an `IRenderTarget` (create, update, remove,
`Flush`) with assets from an `IAssetProvider`. `TextRenderer` is the same job against
a character grid. `PlayerInputTranslator` turns a `PlayerIntent` into commands,
`TilePicker` picks a tile from a ray (over the `TileHeights` planes), and `TorchLayout` and `RoomWalls` compute
placement. Small rules both engine adapters share: `HudPointerMap` (whether the pointer is over the HUD), `GameFolders`
(profile, settings, saves and hi-score locations), `LaunchArguments`, `LevelSubtitles`, `PointerGestures.CancelAll`
(focus loss), `TickPacer` (tick debt, the frame clamp, ticks per frame), `HudToolState` (the tool, the panel
choice, the query tool, the selected corner) and `HudActionRouter` (a HUD action's gate, cancels and corners).

Targets `netstandard2.1`. Depends on `DK2.Core`, `DK2.Data.Model`, `DK2.Simulation`
and `DK2.Assets`. The Unity adapter (`unity/README.md`) implements `IRenderTarget`.

Test: `dotnet test tests/DK2.Presentation.Tests`.
