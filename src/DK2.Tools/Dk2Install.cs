namespace DK2.Tools;

/// <summary>
/// Locates the local Dungeon Keeper 2 installation used as reference material.
/// Resolution order: DK2_DIR environment variable, then dk2.local.json next to
/// the repo root, then known default paths.
/// </summary>
public static class Dk2Install
{
    private static readonly string[] DefaultCandidates =
    [
        @"E:\GogGames\Dungeon Keeper 2",
    ];

    public static string? Find()
    {
        string? env = Environment.GetEnvironmentVariable("DK2_DIR");
        if (IsValid(env)) return env;

        foreach (string candidate in DefaultCandidates)
            if (IsValid(candidate)) return candidate;

        return null;
    }

    public static string Require() =>
        Find() ?? throw new InvalidOperationException(
            "Dungeon Keeper 2 installation not found. Set the DK2_DIR environment " +
            "variable to the game directory (the one containing DKII.EXE).");

    private static bool IsValid(string? dir) =>
        !string.IsNullOrEmpty(dir)
        && File.Exists(Path.Combine(dir, "DKII.EXE"))
        && Directory.Exists(Path.Combine(dir, "Data"));
}
