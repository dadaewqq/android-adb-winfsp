// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

sealed class ConsoleLifetime : IDisposable
{
    delegate bool Handler(uint control);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
    readonly Handler handler;
    readonly ManualResetEventSlim finished = new();
    readonly ConsoleCancelEventHandler cancelHandler;
    public ConsoleLifetime(ManualResetEventSlim stop, Action cancelIo)
    {
        cancelHandler = (_, e) => { e.Cancel = true; stop.Set(); };
        Console.CancelKeyPress += cancelHandler;
        handler = control =>
        {
            if (control is not (2 or 5 or 6)) return false;
            stop.Set();
            try { cancelIo(); } catch { }
            // Windows imposes a short deadline when closing a console window.
            finished.Wait(TimeSpan.FromSeconds(4));
            return true;
        };
        if (!SetConsoleCtrlHandler(handler, true)) throw new IOException("无法注册窗口关闭处理程序。");
    }
    public void Complete() => finished.Set();
    public void Dispose()
    {
        finished.Set();
        SetConsoleCtrlHandler(handler, false);
        Console.CancelKeyPress -= cancelHandler;
        GC.KeepAlive(handler);
    }
}
