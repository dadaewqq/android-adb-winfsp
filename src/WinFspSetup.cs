// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

static class WinFspSetup
{
    public const string InstallerName = "winfsp-2.1.25156.msi";
    public const string InstallerHash = "073a70e00f77423e34bed98b86e600def93393ba5822204fac57a29324db9f7a";

    public static string InstalledDirectory()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = machine.OpenSubKey(@"SOFTWARE\WinFsp");
        string path = key?.GetValue("InstallDir") as string;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(Path.Combine(path, "bin", "winfsp-x64.dll"))) return null;
        using var system = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var services = system.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        bool driver = services != null && services.GetSubKeyNames().Any(name =>
            name.Equals("WinFsp", StringComparison.OrdinalIgnoreCase) || name.StartsWith("WinFsp+", StringComparison.OrdinalIgnoreCase));
        return driver ? path : null;
    }

    public static void ExtractInstaller(string destination)
    {
        using var source = typeof(WinFspSetup).Assembly.GetManifestResourceStream("AndroidAdbFs.WinFspInstaller")
            ?? throw new IOException("此版本未内嵌 WinFsp 安装包。");
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        source.CopyTo(file);
        file.Position = 0;
        string hash = Convert.ToHexStringLower(SHA256.HashData(file));
        if (hash != InstallerHash) throw new IOException("WinFsp 安装包校验失败，已停止安装。");
        file.Flush(true);
    }

    public static bool EnsureInstalled(bool interactive, bool explicitInstall = false)
    {
        string installed = InstalledDirectory();
        if (installed != null)
        {
            Console.WriteLine($"已检测到 WinFsp：{installed}（跳过安装）");
            return true;
        }
        if (!interactive && !explicitInstall) throw new IOException("未安装 WinFsp。请双击本程序，或运行 --install-winfsp，安装后再挂载。");
        if (!explicitInstall)
        {
            Console.WriteLine("\n未检测到完整的 WinFsp 安装。是否安装内嵌的官方 WinFsp 2.1？");
            Console.WriteLine("需要管理员权限，将弹出 UAC；仅安装运行组件，不自动重启。");
            Console.Write("输入 Y 安装，其他输入取消：");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException();
        }
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidAdbFs", "Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string msi = Path.Combine(folder, InstallerName), log = Path.Combine(folder, "install.log");
        ExtractInstaller(msi);
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"))
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"/i \"{msi}\" /passive /norestart ADDLOCAL=F.User,F.Main /L*v \"{log}\""
            };
            Console.WriteLine("正在安装 WinFsp，请在 UAC 中允许。安装日志：" + log);
            using var process = Process.Start(start) ?? throw new IOException("无法启动 Windows Installer。");
            process.WaitForExit();
            if (process.ExitCode == 1602) throw new OperationCanceledException();
            if (process.ExitCode == 3010 || process.ExitCode == 1641)
            {
                Console.WriteLine("WinFsp 安装完成，但 Windows 要求重启。请保存工作并手动重启，然后再次运行本程序。");
                return false;
            }
            if (process.ExitCode != 0) throw new IOException($"WinFsp 安装失败，退出码 {process.ExitCode}。日志：{log}");
            if (InstalledDirectory() == null) throw new IOException($"安装结束后仍未检测到 WinFsp，请检查日志并重启后重试：{log}");
            Console.WriteLine("WinFsp 安装完成，可继续挂载。");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new OperationCanceledException("已取消 UAC 安装授权。"); }
        finally
        {
            // Installer has finished reading the payload; keep its diagnostic log.
            try { File.Delete(msi); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
