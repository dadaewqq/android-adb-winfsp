# Android ADB WinFsp 文件盘

把 Android 手机的共享存储挂载成 Windows **只读盘符**，让资源管理器、文件统计工具和普通桌面程序直接访问手机文件。

电脑端使用 C# / .NET 10 和 WinFsp，直接通过 ADB Sync 协议读取手机。**手机不需要安装 APK，不需要 root。**

## 下载与使用

在 [Releases](https://github.com/dadaewqq/android-adb-winfsp/releases/latest) 下载 `AndroidAdbFs-windows-x64.zip`，完整解压到一个可写目录。

### 环境要求

- Windows x64，安装 [.NET 10 x64 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。
- 安装 [WinFsp](https://winfsp.dev/)，默认的运行组件即可；编译源码才需要 Developer 组件。
- 电脑有可用的 ADB；手机开启 USB 调试并允许此电脑调试。
- 手机的 ADB 实现支持 `stat_v2` 和 `ls_v2`。程序连接本机默认 ADB 服务 `127.0.0.1:5037`。

ADB、.NET Runtime 和 WinFsp 安装包均不包含在下载包内。

### 第一次启动

1. 连接手机，在手机上确认 USB 调试授权。
2. 运行 `adb devices`，确认目标设备状态为 `device`。
3. 双击 `start.cmd`，默认挂载到 `K:` 并打开资源管理器。
4. 使用结束后双击 `stop.cmd`，正常卸载后再拔线。

启动会在后台运行挂载程序，无需一直开着命令窗口。请通过停止脚本卸载，不要直接删除目录或强制结束进程。

## 配置

用记事本编辑解压目录中的 `config.json`，停止后重新启动生效：

```json
{
  "Drive": "K:",
  "AdbPath": "",
  "Serial": "",
  "Root": "/sdcard"
}
```

| 字段 | 说明 |
| --- | --- |
| `Drive` | 空闲盘符，例如 `K:` 或 `M:`。已占用时会报错，不会覆盖其他盘。 |
| `AdbPath` | `adb.exe` 完整路径；留空时查找 PATH、`ANDROID_HOME`、`ANDROID_SDK_ROOT` 和默认 Android SDK 目录。 |
| `Serial` | 目标手机的 ADB 序列号；留空时仅在恰好有一台已授权设备时自动选择。 |
| `Root` | 手机导出目录，默认 `/sdcard`，即共享存储。 |

ADB 没有加入 PATH 时，可指定路径，例如：

```json
{
  "Drive": "K:",
  "AdbPath": "E:/platform-tools/adb.exe",
  "Serial": "",
  "Root": "/sdcard"
}
```

JSON 中建议使用 `/` 表示路径分隔符；使用反斜杠时必须写成 `\\`。

## 连接多台手机

先执行 `adb devices`，复制需要挂载的设备序列号，填入 `Serial`。

```text
List of devices attached
PHONE_A    device
PHONE_B    device
```

例如把 `PHONE_A` 挂载到 K 盘：

```json
{
  "Drive": "K:",
  "AdbPath": "",
  "Serial": "PHONE_A",
  "Root": "/sdcard"
}
```

同时挂载多台：把解压目录复制成两份，分别配置不同的 `Serial` 和 `Drive`，再分别启动。每个目录保存各自的进程记录、停止信号和日志；请在对应目录运行 `stop.cmd`。不要让两个实例使用同一个盘符。

## 能做什么及限制

- 浏览目录、复制文件到电脑、运行支持普通盘符的文件统计工具。
- **只读**：不支持通过该盘写入、删除、重命名或修改手机文件。
- 能访问的路径取决于手机 ADB 权限；非 root 手机的系统目录、应用私有目录等可能无法访问。
- 目录元数据缓存约 3 秒，手机上发生的修改可能稍后显示。
- 使用 ADB Sync 流式读取；向后跳转可能重新读取并丢弃前面的内容，大文件随机读取比带手机端服务的方案慢。
- 断线后不自动重连；请停止挂载，重新连接后再启动。
- 不支持远程 ADB 服务或自定义 ADB 服务端口。

## 实测性能

以下是同一台 Android 手机、同一 USB 连接下的两轮平均值，作为使用参考，不代表所有设备。K 盘是本项目，L 盘是另一个需要手机 APK 的 AFS 项目。

| 测试 | 本项目 | AFS | adb pull |
| --- | ---: | ---: | ---: |
| 复制 107.7 MB 文件 | 2.76 秒 | 4.60 秒 | 2.80 秒 |
| 复制 256 个小文件，共约 9 MB | 1.66 秒 | 5.54 秒 | 1.59 秒 |
| 遍历 Pictures，5994 个文件 | 1.93 秒 | 2.06 秒 | — |
| 在同一大文件中随机读取 8 次，每次 64 KiB | 7.09 秒 | 0.137 秒 | — |

复制后的 SHA-256 均验证通过；校验和清理不计入耗时。电脑和手机缓存未清空，两轮顺序相反。目录遍历包含获取文件大小；随机读取使用相同偏移及内容校验。这里只测试了读取，没有测试写入。

## 常见问题

**找不到 ADB**：在 `config.json` 设置 `AdbPath`。

**`unauthorized` / `offline` / 没有设备**：先检查 `adb devices`，在手机上允许调试；必要时检查数据线、USB 模式和厂商 ADB 驱动。

**提示多个设备**：填写 `Serial`；只连接一台已授权设备时可以留空。

**K 盘已占用**：修改 `Drive` 为其他空闲盘符。

**提示缺少 .NET 或 WinFsp**：安装 x64 .NET 10 Runtime 和 WinFsp，再启动。

**不支持 `stat_v2` / `ls_v2`**：检查手机和电脑 ADB 版本；程序未实现旧协议回退。

**挂载失败或目录不可访问**：查看 `stderr.log`、`stdout.log`。日志可能包含设备序列号或文件路径，分享前请按需要脱敏。

## 从源码编译与打包

安装 .NET 10 SDK 和 WinFsp（包含 Developer 组件），在项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

WinFsp 不在默认目录时：

```powershell
./build.ps1 -WinFspDir 'D:/Tools/WinFsp'
```

输出为 `dist/AndroidAdbFs-windows-x64.zip`，程序仍依赖目标电脑的 .NET 10 Runtime 和 WinFsp。

也可以直接运行命令行程序：

```powershell
./app/AndroidAdbFs.exe --adb 'E:/platform-tools/adb.exe' --mount M: --root /sdcard --serial PHONE_A
```

命令行前台运行时按 Ctrl+C 正常卸载。脚本模式通过 `--stop-file` 实现后台进程的正常停止。

## 工作方式

`Windows 文件操作 → WinFsp → C# 文件系统 → 本机 ADB 服务 → USB → 手机 adbd`

目录读取使用 ADB Sync v2，批量获取目录项及属性。数据读取使用 Sync `RECV`，复用完整事务结束后的连接；中途停止读取的连接会被丢弃。程序没有额外的整文件内容缓存。

## 许可与致谢

本项目使用 GPL-3.0-or-later，见 `LICENSE`。

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos。
WinFsp 仓库：<https://github.com/winfsp/winfsp>；其许可与例外条款保留在 `third_party/WinFsp-License.txt`，发布包亦附带该文件。WinFsp 不属于本项目自制代码。
