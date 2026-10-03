// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;

namespace DK2.Data.Kwd;

/// <summary>
/// One chunk of a KWD/KLD file. A file may contain several chunks back to back
/// (e.g. GlobalVariables.kwd carries more than one VARIABLES chunk).
/// </summary>
public sealed record KwdChunk
{
    /// <summary>Original container words; null means no encoding template was supplied.</summary>
    public KwdContainerHeader? ContainerHeader { get; init; }
    public required KwdRecordType Type { get; init; }
    public required int ItemCount { get; init; }
    /// <summary>Raw record payload (contentSize bytes; records are packed back to back).</summary>
    public required byte[] Content { get; init; }
    /// <summary>Raw type-specific header block (headerEndOffset bytes), for MAP/LEVEL metadata.</summary>
    public required byte[] HeaderBlock { get; init; }
}

/// <summary>Header words retained separately from the complete, possibly understated payload.</summary>
public sealed record KwdContainerHeader(uint ChunkEndOffset, uint CheckTwo,
    uint DeclaredContentSize, int SourceContentLength);
