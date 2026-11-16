# DK2.Art

Readers for the player's own original art (ADR-0408): `KmfReader` and the KMF model types,
`EngineTextureArchive` and the texture decoder, the `IMeshSource`/`ITextureSource` sources over
`Meshes.WAD` and `EngineTextures.dat`, and `Dk2ArtCatalogue`, the art-resource names in the
KWD definitions.

`ArtSourceMap` maps every key `AssetCatalogue` lists to the KMF that answers it, or the reason
none does; the converter writes those entries out and `InstallArtProvider` reads them in place.
`InstallArtProvider` is the converted tier over the install: it resolves a key to
`AssetSource.InstallEntry` and loads the model and its textures, decoding each once.

Targets `netstandard2.1` so the Unity adapter can load it; depends on `DK2.Data`. It writes
nothing: the GLB and PNG writers are `DK2.Convert`'s. Original files and anything decoded from
them stay out of the repository.

`CreatureAnimationPlayer<TResources>` plays one creature's or object's type-6 clip on the draw clock: the deferred
replacement, the start tick, the turn catch-up, the loop beat, the footstep marks, the health phase and the tagged control
points, over an engine's `ICreatureAnimationSurface` (install, show, upload). `CreatureAnimationArt` holds the art rules for
starting a clip. The pose and turn rules it is driven by are in `DK2.Presentation` (`PoseInterpolator`, `AnimationClipRequest`).

Format notes: `src/DK2.Convert/README.md`. Tests: `dotnet test tests/DK2.Convert.Tests`
(`ArtArchitectureTests` checks the target framework and the references).

`WadImageSource` supplies BMP24 and unmasked `.444` images as top-down RGBA,
using an injected PNG decoder (`InstallFrontEndSources` in `DK2.Presentation` takes each engine's). Indexed reads
preserve duplicate names; name lookup retains the archive's last-match behavior.
The source owns its archive; callers own textures returned by the Unity adapter.
