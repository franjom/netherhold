// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DK2.Data.Model;
using Microsoft.Win32.SafeHandles;

namespace DK2.Data.Campaign;

/// <summary>Optional Windows import. Opens the original key for query access only, never creates it.</summary>
public static class InstalledCampaignRegistry
{
    public const string PlayerKeyPath = @"Software\Bullfrog Productions Ltd\Dungeon Keeper II\Configuration\Player";
    private static readonly (string Name, bool Binary)[] Values =
    {
        ("Version Number", false), ("Level Number", false), ("MPD Level Number", false),
        ("Transfer Creature ID", false), ("Transfer Creature Level", false), ("Secret Level Number", false),
        ("Player Level Status", true), ("Total Evil Rating", true), ("Level Attempts", true), ("Secret Levels", true),
        ("Secret Levels Completed", true), ("Special Levels Completed", true), ("Pet Dungeon Levels Completed", true),
    };

    public static CampaignProfile? ReadCurrentUser()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
        int error = RegOpenKeyEx(new IntPtr(unchecked((int)0x80000001)), PlayerKeyPath, 0, 1, out KeyHandle key);
        using (key)
        {
            if (error == 2) return null;
            if (error != 0) throw new Win32Exception(error);
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach ((string name, bool binary) in Values)
            {
                uint size = 0;
                error = RegQueryValueEx(key, name, IntPtr.Zero, out _, null, ref size);
                if (error == 2) continue;
                if (error != 0) throw new Win32Exception(error);
                if (size > 4096) throw new InvalidDataException($"Registry campaign value too large: {name}");
                byte[] bytes = new byte[size];
                error = RegQueryValueEx(key, name, IntPtr.Zero, out uint type, bytes, ref size);
                if (error != 0) throw new Win32Exception(error);
                if (size != bytes.Length) throw new IOException($"Registry value changed during import: {name}");
                // DK2's binary query ignores the registry type; some installed arrays use type 9.
                if (binary) values.Add(name, bytes);
                else if (type == 4 && size == 4) values.Add(name, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
                // Missing or unsupported scalar type/width retains the profile default.
            }
            return CampaignRegistryImport.Read(values);
        }
    }

    private sealed class KeyHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public KeyHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => RegCloseKey(handle) == 0;
    }

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegOpenKeyEx(IntPtr root, string path, uint options, uint access, out KeyHandle key);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegQueryValueEx(KeyHandle key, string name, IntPtr reserved,
        out uint type, [Out] byte[]? data, ref uint size);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern int RegCloseKey(IntPtr key);
}
