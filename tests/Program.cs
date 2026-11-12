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
        string folder = Path.Combine(Path.GetTempPath(), "AndroidAdbFs-BootstrapTest-" + Guid.NewGuid().ToString("N"));
        string registryPath = @"Software\AndroidAdbFsTests\" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(folder);
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
            Console.SetIn(new StringReader("n\n"));
            bool canceled = false;
            try { WinFspSetup.EnsureInstalled(true); } catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "Declining installation cancels before UAC or MSI launch");
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
