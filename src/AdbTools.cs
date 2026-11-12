// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

static class AdbTools
{
    public const string Version = "37.0.1";
    static readonly (string Name, string Hash)[] Files =
    [
        ("adb.exe", "b4a6b455702684652cccf7b46258b29e653538904359a58fd4931cf3ef286b3f"),
        ("AdbWinApi.dll", "c1d653030b4bde65d3e07e4d0b0979e17be56df1436cdd15528630f27808050d"),
        ("AdbWinUsbApi.dll", "0710e894d9b40f71a670c13c694079d564c92c1279da382cfe4850983aaebe1b"),
        ("NOTICE.txt", "38ec8c6f5b7799c223ffeab1f9e81c2d5fc67b5e56d6424f649630ca1ee1a811")
    ];

    static bool Matches(string path, string expected)
    {
        if (!File.Exists(path)) return false;
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file)) == expected;
    }

    public static string EnsureAvailable(string directory = null)
    {
        directory = Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidAdbFs", "Tools", "adb-" + Version));
        Directory.CreateDirectory(directory);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..24];
        using var gate = new Mutex(false, @"Local\AndroidAdbFs.Adb." + key);
        bool owned = false;
        try
        {
            try { owned = gate.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new IOException("等待其他实例释放内置 ADB 超时，请稍后重试。");
            foreach (var item in Files)
            {
                string path = Path.Combine(directory, item.Name);
                if (Matches(path, item.Hash)) continue;
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var resource = typeof(AdbTools).Assembly.GetManifestResourceStream("AndroidAdbFs.Adb." + item.Name)
                        ?? throw new IOException("缺少内置 ADB 资源：" + item.Name))
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        resource.CopyTo(file);
                        file.Flush(true);
                    }
                    if (!Matches(temporary, item.Hash)) throw new IOException("内置 ADB 文件校验失败：" + item.Name);
                    File.Move(temporary, path, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        finally { if (owned) gate.ReleaseMutex(); }
        return Path.Combine(directory, "adb.exe");
    }
}
