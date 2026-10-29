// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Text;
using DK2.Core;

namespace DK2.Assets;

/// <summary>What sort of thing an <see cref="AssetRef"/> points at.</summary>
///
/// <remarks>
/// The list is AGENTS.md goal 4's own list of what must be replaceable —
/// models, textures, animations, sounds, music, video, effects — and nothing
/// beyond it. A kind earns its place by being a thing a provider resolves
/// differently, not by being a word somebody might want.
/// </remarks>
public enum AssetKind
{
    Mesh,
    Texture,
    Animation,
    Sound,
    Music,
    Video,
    Effect,
}

/// <summary>
/// A logical asset key — not a filename: it names what the game needs ("the imp's
/// mesh") and a provider decides where that comes from, so no path to a proprietary
/// file exists outside a provider (ADR-0034).
/// </summary>
/// <remarks>
/// <para>Shape: <c>category.subject.role[.variant]</c>, lowercase, dot separated,
/// <c>[a-z0-9_]</c> per segment — <c>creature.imp.animation.walk</c>. Segments are
/// validated, not trusted: a mistyped key would else resolve to a placeholder.</para>
/// <para>Keys come from a record's <b>name</b>, never its id (an id is a fact about
/// the reference install). Same rule as <c>TileWorkRules</c>.</para>
/// </remarks>
public readonly struct AssetRef : IEquatable<AssetRef>
{
    /// <summary>The categories in use. Kept closed so a typo cannot invent one.</summary>
    public const string TerrainCategory = "terrain";
    public const string RoomCategory = "room";
    public const string CreatureCategory = "creature";

    /// <summary>
    /// A thing standing on a tile: a bed, a chicken coop, a pile of gold. One of
    /// the 145 <c>Objects.kwd</c> records — and it is a category of its own
    /// rather than part of <see cref="RoomCategory"/> because an object outlives
    /// the room it was placed for. A bed belongs to the creature that dropped
    /// it, not to the lair, and a pile of gold on bare floor belongs to no room
    /// at all.
    /// </summary>
    public const string ObjectCategory = "object";

    public const string UiCategory = "ui";
    public const string DoorCategory = "door";
    public const string TrapCategory = "trap";
    public const string EffectElementCategory = "effect_element";
    public const string EffectCategory = "effect";

    /// <summary>A Meshes.WAD entry no record names, keyed by the entry itself: a member of an
    /// effect's group art (the Heart plug's nine pieces).</summary>
    public const string MeshEntryCategory = "mesh_entry";

    private static readonly HashSet<string> Categories =
        new(StringComparer.Ordinal)
        {
            TerrainCategory, RoomCategory, CreatureCategory, ObjectCategory, UiCategory, DoorCategory, TrapCategory,
            EffectElementCategory, EffectCategory, MeshEntryCategory,
        };

    private readonly string? _key;

    private AssetRef(string key, AssetKind kind)
    {
        _key = key;
        Kind = kind;
    }

    /// <summary>The full key, e.g. <c>creature.imp.mesh</c>. Never null once created.</summary>
    public string Key => _key ?? throw new InvalidOperationException(
        "A default AssetRef has no key. Build one through Create or a For* helper.");

    /// <summary>What kind of asset this is. Part of the identity, not a lookup.</summary>
    public AssetKind Kind { get; }

    /// <summary>True for a <c>default</c> value, which is not a usable reference.</summary>
    public bool IsEmpty => _key is null;

    /// <summary>
    /// Builds a reference from its parts, normalising the subject and validating
    /// everything. Throws on anything that would not round-trip, because the
    /// alternative is a key that resolves to a placeholder and never complains.
    /// </summary>
    public static AssetRef Create(string category, string subject, AssetKind kind, string? variant = null)
    {
        if (!Categories.Contains(category))
            throw new ArgumentException(
                $"'{category}' is not a known asset category. Add it to AssetRef if it is real.",
                nameof(category));

        string normalisedSubject = NormaliseSubject(subject);
        string role = RoleOf(kind);

        var key = new StringBuilder(category).Append('.').Append(normalisedSubject).Append('.').Append(role);

        if (variant is not null)
        {
            string normalisedVariant = NormaliseSubject(variant);
            key.Append('.').Append(normalisedVariant);
        }

        return new AssetRef(key.ToString(), kind);
    }

    /// <summary>The mesh, texture or effect belonging to a terrain type.</summary>
    public static AssetRef ForTerrain(string terrainName, AssetKind kind, string? variant = null) =>
        Create(TerrainCategory, terrainName, kind, variant);

    /// <summary>The art belonging to a room type.</summary>
    public static AssetRef ForRoom(string roomName, AssetKind kind, string? variant = null) =>
        Create(RoomCategory, roomName, kind, variant);

    /// <summary>The art belonging to a creature type.</summary>
    public static AssetRef ForCreature(string creatureName, AssetKind kind, string? variant = null) =>
        Create(CreatureCategory, creatureName, kind, variant);

    /// <summary>The art belonging to an object type.</summary>
    public static AssetRef ForObject(string objectName, AssetKind kind, string? variant = null) =>
        Create(ObjectCategory, objectName, kind, variant);

    public static AssetRef ForDoor(string doorName, bool open = false) =>
        Create(DoorCategory, doorName, AssetKind.Mesh, open ? "open" : "closed");

    public static AssetRef ForDoorLock(string doorName) =>
        Create(DoorCategory, doorName, AssetKind.Mesh, "lock");

    public static AssetRef ForTrap(string trapName) => Create(TrapCategory, trapName, AssetKind.Mesh);

    public static AssetRef ForEffectElement(string elementName) =>
        Create(EffectElementCategory, elementName, AssetKind.Effect);

    public static AssetRef ForEffect(string effectName) =>
        Create(EffectCategory, effectName, AssetKind.Effect);

    /// <summary>
    /// The role segment for a kind. Deliberately a total function over the enum,
    /// so adding a kind without deciding its role does not compile.
    /// </summary>
    public static string RoleOf(AssetKind kind) => kind switch
    {
        AssetKind.Mesh => "mesh",
        AssetKind.Texture => "texture",
        AssetKind.Animation => "animation",
        AssetKind.Sound => "sound",
        AssetKind.Music => "music",
        AssetKind.Video => "video",
        AssetKind.Effect => "effect",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No role segment for this asset kind."),
    };

    /// <summary>
    /// The punctuation shipped record names use, each mapping to one underscore.
    /// </summary>
    /// <remarks>
    /// Measured: sweeping the reference install's 41 terrain, 26 room and 46 creature
    /// names for anything outside <c>[A-Za-z0-9 ]</c> returns exactly
    /// <c>&lt; &gt; [ ]</c>, from editor annotations such as <c>Hero Gate [ Final ]</c>
    /// and <c>Mercenary Gate [&lt;not used&gt;]</c>. Dash and apostrophe are included
    /// for names a mod or a later record type may carry. Pinned by
    /// <c>AssetCatalogueTests</c>, which normalises every name in the install.
    /// </remarks>
    private const string Separators = " -'[]<>()";

    /// <summary>
    /// Turns a record's name into a key segment: lowercase, punctuation to one
    /// underscore, runs collapsed, edges trimmed (<c>Hero Gate [ Final ]</c> →
    /// <c>hero_gate_final</c>); everything else refused.</summary>
    /// <remarks>
    /// Refusing rather than stripping is load-bearing twice: a name carrying <c>.</c>,
    /// <c>/</c>, <c>\</c> or a control character did not come from a record, so
    /// sanitising it would mint a believable key for the wrong thing; and it makes
    /// <see cref="FileAssetProvider"/>'s dot-to-separator mapping safe (no separator or
    /// dot in a segment, so no escape from the asset root). <c>AssetCatalogueTests</c>
    /// asserts collapsing collides no two shipped names.
    /// </remarks>
    public static string NormaliseSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("An asset key segment cannot be blank.", nameof(subject));

        string trimmed = subject.Trim();
        var sb = new StringBuilder(trimmed.Length);
        bool pendingSeparator = false;

        foreach (char c in trimmed)
        {
            char mapped;
            if (c is >= 'a' and <= 'z' || c is >= '0' and <= '9')
                mapped = c;
            else if (c is >= 'A' and <= 'Z')
                mapped = (char)(c + 32);
            else if (c == '_' || Separators.IndexOf(c) >= 0)
                mapped = '_';
            else
                throw new ArgumentException(
                    $"'{subject}' cannot be an asset key segment: '{c}' has no mapping. " +
                    "Asset keys come from record names, so an unmappable character usually " +
                    "means the name was read from the wrong place.",
                    nameof(subject));

            if (mapped == '_')
            {
                // Collapse a run, and never lead with one.
                pendingSeparator = sb.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                sb.Append('_');
                pendingSeparator = false;
            }

            sb.Append(mapped);
        }

        // A trailing separator is dropped by simply never flushing it.
        string result = sb.ToString();
        if (result.Length == 0)
            throw new ArgumentException(
                $"'{subject}' normalises to nothing usable.", nameof(subject));

        return result;
    }

    /// <summary>
    /// A stable 64-bit hash of the key. Used by <see cref="PlaceholderProvider"/>
    /// to give every asset a consistent placeholder appearance, so the same
    /// terrain is the same colour in every run and on every machine — which is
    /// what makes a box-only view readable rather than a kaleidoscope.
    /// </summary>
    public ulong StableHash()
    {
        Fnv1a64 hash = Fnv1a64.Start();
        hash.Add(Key);
        hash.Add((int)Kind);
        return hash.Value;
    }

    public bool Equals(AssetRef other) =>
        Kind == other.Kind && string.Equals(_key, other._key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AssetRef other && Equals(other);

    // Not the structural hash: this one only has to be consistent within a
    // process, and StableHash exists for the case that must not be.
    public override int GetHashCode() =>
        unchecked(((_key?.GetHashCode() ?? 0) * 397) ^ (int)Kind);

    public override string ToString() => _key ?? "<empty AssetRef>";

    public static bool operator ==(AssetRef left, AssetRef right) => left.Equals(right);
    public static bool operator !=(AssetRef left, AssetRef right) => !left.Equals(right);
}
