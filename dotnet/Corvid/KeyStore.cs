// Copyright (c) 2026 AqueGen. All rights reserved. See LICENSE.
using System.Security.Cryptography;

namespace Corvid;

public static class KeyStore
{
    public static ChannelKey LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var plain = ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(path)), null, DataProtectionScope.CurrentUser);
                return ChannelKey.FromHex(Convert.ToHexStringLower(plain));
            }
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
        }

        var key = ChannelKey.Generate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Convert.ToBase64String(ProtectedData.Protect(key.Bytes, null, DataProtectionScope.CurrentUser)));
        File.Move(temp, path, true);
        return key;
    }
}
