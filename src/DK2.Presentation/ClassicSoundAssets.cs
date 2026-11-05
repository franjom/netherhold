// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation;

/// <summary>The installed sound maps and banks. Implementations are called from decode workers
/// (<see cref="SoundClipDecoder"/>) as well as the frame's thread, so they must be thread-safe.</summary>
public interface IClassicSoundAssets
{
    SoundMaps Maps(SoundCategory category);
    PcmClip? Clip(ResolvedSound reference);
}

/// <summary>Reads installed sound maps and banks; session caches contain only in-memory data. Thread-safe: maps and
/// banks are each read once under their own lock, and clips decode outside both.</summary>
public sealed class InstallClassicSoundAssets : IClassicSoundAssets
{
    private readonly string _root;
    private readonly Dictionary<SoundCategory, SoundMaps> _maps = new();
    private readonly Dictionary<string, SoundBank> _banks = new(StringComparer.OrdinalIgnoreCase);
    public InstallClassicSoundAssets(string install) => _root = Path.Combine(install, "Data", "Sound", "SFX");

    public SoundMaps Maps(SoundCategory category)
    {
        Validate(category.Name, false);
        lock (_maps)
        {
            if (!_maps.TryGetValue(category, out SoundMaps? maps))
            {
                string directory = Path.Combine(_root, category.Name.StartsWith("speech_", StringComparison.OrdinalIgnoreCase) ? category.Name : "Global");
                string bank = Path.Combine(directory, category.Name + "BANK.map"), sfx = Path.Combine(directory, category.Name + "SFX.map");
                // A category the data names but the install does not ship (OBJECT_TRAINING_TARGET, object 29
                // Training Target's) finds no handle in DK2's manager lookup, so its requests sound nothing; here it is empty.
                maps = File.Exists(bank) && File.Exists(sfx)
                    ? new SoundMaps(BankMap.Read(File.ReadAllBytes(bank)), SfxMap.Read(File.ReadAllBytes(sfx)))
                    : new SoundMaps(BankMap.Empty, SfxMap.Empty);
                _maps.Add(category, maps);
            }
            return maps;
        }
    }

    public PcmClip? Clip(ResolvedSound reference)
    {
        Validate(reference.Archive, true);
        SoundBank? bank;
        lock (_banks)
        {
            if (!_banks.TryGetValue(reference.Archive, out bank))
            {
                string path = reference.Archive.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                bank = new SoundBank(SdtArchive.Open(Path.Combine(_root, path + "HD.sdt")));
                _banks.Add(reference.Archive, bank);
            }
        }
        return bank.GetClip(reference.Entry);
    }

    private static void Validate(string name, bool allowSeparator)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !(char.IsLetterOrDigit(c) || c == '_'
            || allowSeparator && (c == '/' || c == '\\')))
            || name[0] == '/' || name[0] == '\\')
            throw new ArgumentException("Invalid sound asset name.", nameof(name));
    }
}
