// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Core;

public enum SaveIncompatibleReason
{
    NotASave = 1,
    UnsupportedVersion = 2,
    Truncated = 3,
    Corrupt = 4,
    OutOfRange = 5,
    ChecksumMismatch = 6,
}

/// <summary>A refused save, with a stable code for the future text catalog (ADR-0587).
/// Exception-base decision: ADR-0588.</summary>
public sealed class SaveIncompatibleException : IOException
{
    public SaveIncompatibleReason Reason { get; }
    public long? Position { get; }

    public SaveIncompatibleException(SaveIncompatibleReason reason, string message,
        long? position = null, Exception? innerException = null)
        : base(position is long offset ? $"{message} (byte {offset})." : message, innerException)
    {
        Reason = reason;
        Position = position;
    }
}
