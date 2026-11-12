// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

sealed class AlreadyMountedException(string message, string drive) : IOException(message)
{
    public string Drive { get; } = drive;
}

sealed class DeviceMountLease : IDisposable
{
    readonly Mutex mutex;
    readonly string statePath;
    public DeviceMountLease(string serial)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial)));
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidAdbFs", "Mounts");
        Directory.CreateDirectory(directory);
        statePath = Path.Combine(directory, key + ".json");
        mutex = new Mutex(false, @"Global\AndroidAdbFs.Device." + key);
        bool owned;
        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            mutex.Dispose();
            string drive = null;
            try { using var state = JsonDocument.Parse(File.ReadAllText(statePath)); drive = state.RootElement.GetProperty("Drive").GetString(); } catch (IOException) { } catch (JsonException) { }
            throw new AlreadyMountedException($"这台手机已经挂载{(drive == null ? "" : "到 " + drive)}，不会重复创建磁盘。", drive);
        }
    }
    public void Record(string serial, string drive)
    {
        string temporary = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { Serial = serial, Drive = drive, ProcessId = Environment.ProcessId }));
        File.Move(temporary, statePath, true);
    }
    public void Dispose()
    {
        try { File.Delete(statePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
