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

Format notes: `src/DK2.Convert/README.md`. Tests: `dotnet test tests/DK2.Convert.Tests`
(`ArtArchitectureTests` checks the target framework and the references).
