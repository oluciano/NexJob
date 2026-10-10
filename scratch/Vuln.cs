// Deliberately vulnerable code to prove that CodeQL detects it (issue #417, test N2). Never merge, never compiled.
using System.IO.Compression;
using System.Security.Cryptography;

namespace Scratch;

public static class Vuln
{
    public static void ExtractAll(string zipPath, string targetDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.Combine(targetDir, entry.FullName);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    public static byte[] WeakEncrypt(byte[] data, byte[] key, byte[] iv)
    {
        using var des = DES.Create();
        using var encryptor = des.CreateEncryptor(key, iv);
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }
}
