# DK2.Assets

The asset abstraction (ADR-0036). Nothing outside it knows a path or a file format. An
`AssetRef` names an asset; `IAssetProvider` resolves it to a `ResolvedAsset`: a file
(`FileAssetProvider`, over converter output), an entry in the player's install
(`InstallEntry`, answered by `DK2.Art`'s `InstallArtProvider`, ADR-0408) or a
`PlaceholderProvider` look. `AssetProviderChain.Standard(replacementRoot, convertedRoot, install)`
chains them in priority order, and `AssetCatalogue` lists the keys the game asks for.

Targets `netstandard2.1`. Depends on `DK2.Core` and `DK2.Data.Model`. It does not
decode DK2 files; `DK2.Art` reads them and `DK2.Convert` produces what `FileAssetProvider`
reads.

Test: `dotnet test tests/DK2.Assets.Tests`.
