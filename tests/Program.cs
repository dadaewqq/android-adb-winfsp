using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

static class Tests
{
    [DllImport("advapi32.dll")]
    static extern int RegOverridePredefKey(nint key, nint replacement);

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static int Main()
    {
        string[] names = ["100%.txt", "hello%20world.txt", "name%3A.txt", "binary 'quote' %.bin", "CON", "aux.txt", "NUL", "name:with?chars", "trailing. ", "测试 文件.txt", "%25", "%43ON", "a\\b", "percent%41"];
        foreach (string name in names) Check(FileNames.Decode(FileNames.Encode(name)) == name, "Filename round-trip: " + name);
        Check(FileNames.Encode("100%.txt") == "100%.txt", "Ordinary percent filenames stay unchanged");
        foreach (string invalid in new[] { "%00", "%2F", ".", ".." })
        {
            bool rejected = false;
            try { FileNames.Decode(invalid); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Unsafe filename rejected: " + invalid);
        }
        var random = new Random(713);
        const string alphabet = "abcABC0123% .:?<>|*'\\测试";
        var encoded = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < 10000; i++)
        {
            string name = new(Enumerable.Range(0, random.Next(1, 24)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            if (name is "." or "..") continue;
            string value = FileNames.Encode(name);
            if (FileNames.Decode(value) != name) throw new Exception("Filename round-trip mismatch: " + name);
            if (encoded.TryGetValue(value, out string other) && other != name) throw new Exception("Filename encoding collision");
            encoded[value] = name;
        }
        Check(true, "10,000 filename round-trips and collision checks");
        string folder = Path.Combine(Path.GetTempPath(), "AndroidAdbFs-BootstrapTest-" + Guid.NewGuid().ToString("N"));
        string registryPath = @"Software\AndroidAdbFsTests\" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
        string adbDirectory = Path.Combine(folder, "adb-cache");
        try
        {
            string adb = AdbTools.EnsureAvailable(adbDirectory);
            Check(File.Exists(adb) && Directory.GetFiles(adbDirectory).Length == 4, "Bundled ADB extracts exactly its required files and notices");
            var timestamp = File.GetLastWriteTimeUtc(adb);
            AdbTools.EnsureAvailable(adbDirectory);
            Check(File.GetLastWriteTimeUtc(adb) == timestamp, "Valid cached ADB is reused without rewriting");
            string dependency = Path.Combine(adbDirectory, "AdbWinApi.dll");
            File.WriteAllText(dependency, "corrupt");
            AdbTools.EnsureAvailable(adbDirectory);
            using var file = File.OpenRead(dependency);
            Check(Convert.ToHexStringLower(SHA256.HashData(file)) == "c1d653030b4bde65d3e07e4d0b0979e17be56df1436cdd15528630f27808050d", "Corrupted ADB cache is restored from embedded resources");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(adbDirectory)) File.Delete(file);
            Directory.Delete(adbDirectory);
        }
        string before = WinFspSetup.InstalledDirectory();
        using var sandbox = Registry.CurrentUser.CreateSubKey(registryPath);
        bool redirected = false;
        try
        {
            string msi = Path.Combine(folder, WinFspSetup.InstallerName);
            WinFspSetup.ExtractInstaller(msi);
            using (var stream = File.OpenRead(msi)) Check(Convert.ToHexStringLower(SHA256.HashData(stream)) == WinFspSetup.InstallerHash, "Embedded MSI is byte-identical to official payload");
            bool refusedOverwrite = false;
            try { WinFspSetup.ExtractInstaller(msi); } catch (IOException) { refusedOverwrite = true; }
            Check(refusedOverwrite, "MSI extraction refuses to overwrite an existing file");

            // Redirect HKLM only within this test process. Never modify machine WinFsp keys.
            int error = RegOverridePredefKey(unchecked((nint)(int)0x80000002), sandbox.Handle.DangerousGetHandle());
            Check(error == 0, "Process-local registry sandbox enabled");
            redirected = true;
            Check(WinFspSetup.InstalledDirectory() == null, "Missing WinFsp is detected");
            bool refusedUnattended = false;
            try { WinFspSetup.EnsureInstalled(false); } catch (IOException) { refusedUnattended = true; }
            Check(refusedUnattended, "Unattended mount does not silently install a driver");
            Console.SetIn(new StringReader(""));
            bool canceled = false;
            int launches = 0;
            string setupFolder = null;
            try
            {
                WinFspSetup.EnsureInstalled(true, installerRunner: start =>
                {
                    launches++;
                    Check(start.Verb == "runas" && start.UseShellExecute, "Automatic installation requests UAC");
                    Check(start.Arguments.Contains("/norestart") && start.Arguments.Contains("ADDLOCAL=F.User,F.Main"), "Installer keeps runtime-only and no-restart options");
                    setupFolder = Path.GetDirectoryName(System.Text.RegularExpressions.Regex.Match(start.Arguments, "/i \\\"([^\\\"]+)\\\"").Groups[1].Value);
                    throw new System.ComponentModel.Win32Exception(1223);
                });
            }
            catch (OperationCanceledException) { canceled = true; }
            Check(launches == 1, "Missing WinFsp automatically enters setup without reading Y confirmation");
            Check(canceled, "Canceling UAC exits without mounting");
            Check(!File.Exists(Path.Combine(setupFolder, WinFspSetup.InstallerName)), "Canceled setup removes the extracted MSI");
            Directory.Delete(setupFolder);
        }
        finally
        {
            if (redirected && RegOverridePredefKey(unchecked((nint)(int)0x80000002), 0) != 0) throw new Exception("Cannot restore process registry mapping");
            sandbox.Close();
            Registry.CurrentUser.DeleteSubKey(registryPath);
            File.Delete(Path.Combine(folder, WinFspSetup.InstallerName));
            Directory.Delete(folder);
        }
        Check(WinFspSetup.InstalledDirectory() == before, "Real WinFsp installation remains unchanged");
        Console.WriteLine("Installer execution/UAC on a clean machine is not covered by this test.");
        return 0;
    }
}
