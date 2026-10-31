// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Text;

namespace Corvid;

public static class AddonKeyFile
{
    public const string FileName = "CorvidKey.lua";

    public static void Write(string addonDir, ChannelKey key)
    {
        Directory.CreateDirectory(addonDir);
        File.WriteAllBytes(
            Path.Combine(addonDir, FileName),
            new UTF8Encoding(false).GetBytes($"local _, ns = ...\nns.corvidKey = \"{key.ToHex()}\"\n"));
    }
}
