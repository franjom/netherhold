# DK2.Core

The deterministic primitives every other assembly stands on: fixed-point `Fp32` and
`FpPosition3`, `TilePos`, `GameTick`, `Hash64` (state hashing), the random generators
(`SimRandom` over `Xoshiro256StarStar` or DK2's own `Dk2Lcg`, behind
`IRandomAlgorithm`), integer math helpers, and the `Confidence` enum with the
`Dk2BehaviorAttribute` that tags code with a research note.

Targets `netstandard2.1` (Unity-bound, ADR-0002, `src/Shared/UnityBound.props`). No
project or package dependencies, and no Unity or file-system access.

Test: `dotnet test tests/DK2.Core.Tests`.
