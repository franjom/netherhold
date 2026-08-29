using DK2.Data.Model;

namespace DK2.Data.Tests;

/// <summary>
/// Smoke tests against the real DK2 installation. Skipped automatically when no
/// installation is present (CI machines), so the build stays green everywhere.
/// </summary>
public class KwdSmokeTests
{
    private static string? FindInstall() => Dk2Install.Find();

    public static bool InstallPresent => FindInstall() is not null;

    [Fact]
    public void EditorDatabaseFiles_StartWithExpectedRecordType()
    {
        string? dir = FindInstall();
        if (dir is null) return; // skipped: no DK2 install on this machine (e.g. CI)

        // First 4 bytes of each editor KWD are the record-type id (little endian).
        // Verified by direct inspection on 2026-08-29; see docs/research/kwd-container.md.
        (string File, KwdRecordType Expected)[] cases =
        [
            ("Creatures.kwd", KwdRecordType.Creatures),
            ("GlobalVariables.kwd", KwdRecordType.Variables),
            ("Rooms.kwd", KwdRecordType.Rooms),
            ("Terrain.kwd", KwdRecordType.Terrain),
            ("Traps.kwd", KwdRecordType.Traps),
            ("Doors.kwd", KwdRecordType.Doors),
            ("KeeperSpells.kwd", KwdRecordType.KeeperSpells),
            ("CreatureSpells.kwd", KwdRecordType.CreatureSpells),
            ("Objects.kwd", KwdRecordType.Objects),
            ("Shots.kwd", KwdRecordType.Shots),
            ("Effects.kwd", KwdRecordType.Effects),
            ("EffectElements.kwd", KwdRecordType.EffectElements),
        ];

        Span<byte> header = stackalloc byte[4];
        foreach ((string file, KwdRecordType expected) in cases)
        {
            string path = Path.Combine(dir!, "Data", "editor", file);
            Assert.True(File.Exists(path), $"missing: {path}");

            using var fs = File.OpenRead(path);
            fs.ReadExactly(header);
            int typeId = BitConverter.ToInt32(header);

            Assert.Equal(expected, (KwdRecordType)typeId);
        }
    }
}
