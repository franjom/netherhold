// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Media;

namespace DK2.Presentation;

public interface IClassicSoundAssets
{
    SoundMaps Maps(SoundCategory category);
    PcmClip? Clip(ResolvedSound reference);
}

/// <summary>Reads installed sound maps and banks; session caches contain only in-memory data.</summary>
public sealed class InstallClassicSoundAssets : IClassicSoundAssets
{
    private readonly string _root;
    private readonly Dictionary<SoundCategory, SoundMaps> _maps = new();
    private readonly Dictionary<string, SoundBank> _banks = new(StringComparer.OrdinalIgnoreCase);
    public InstallClassicSoundAssets(string install) => _root = Path.Combine(install, "Data", "Sound", "SFX");

    public SoundMaps Maps(SoundCategory category)
    {
        Validate(category.Name, false);
        if (!_maps.TryGetValue(category, out SoundMaps? maps))
        {
            string directory = Path.Combine(_root, category.Name.StartsWith("speech_", StringComparison.OrdinalIgnoreCase) ? category.Name : "Global");
            maps = new SoundMaps(BankMap.Read(File.ReadAllBytes(Path.Combine(directory, category.Name + "BANK.map"))),
                SfxMap.Read(File.ReadAllBytes(Path.Combine(directory, category.Name + "SFX.map"))));
            _maps.Add(category, maps);
        }
        return maps;
    }

    public PcmClip? Clip(ResolvedSound reference)
    {
        Validate(reference.Archive, true);
        if (!_banks.TryGetValue(reference.Archive, out SoundBank? bank))
        {
            string path = reference.Archive.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            bank = new SoundBank(SdtArchive.Open(Path.Combine(_root, path + "HD.sdt")));
            _banks.Add(reference.Archive, bank);
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
