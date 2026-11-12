# Android ADB WinFsp 文件盘

将 Android 手机的共享存储挂载为 Windows **只读磁盘**，默认显示为 `K:`。挂载后，可以用资源管理器浏览文件、复制到电脑，也可以用支持普通盘符的工具统计目录大小。

**手机不用安装 APK，不要求 root；电脑需要 ADB 和 WinFsp。** 可访问哪些目录，取决于手机授予 ADB 的权限。

## 1. 先选下载版本

前往 **[最新版本下载页](https://github.com/dadaewqq/android-adb-winfsp/releases/latest)**，在页面的 **Assets** 中选择一个 ZIP：

| 你的电脑环境 | 选择的文件 | ZIP 大小 |
| --- | --- | ---: |
| 已安装 .NET 10 x64 Runtime | `AndroidAdbFs-windows-x64-framework-dependent.zip` | 约 186 KiB |
| 没有安装 .NET，或不想额外安装 | `AndroidAdbFs-windows-x64-self-contained.zip` | 约 34.3 MiB |

两个版本功能完全相同，任选一个即可：

- **依赖版（framework-dependent）**：包内不带 .NET，使用电脑上已安装的 .NET 10 x64 Runtime。
- **自包含版（self-contained）**：包内自带 .NET 运行时，电脑不需要另装 .NET；无需改动电脑已有的 .NET。
- **两个版本都不包含 ADB 和 WinFsp，仍需自行准备。** 自包含版不是 Native AOT。

> 不要下载页面自动生成的 `Source code (zip)` 来运行；那是源码。请下载上表中以 `AndroidAdbFs-` 开头的文件。

## 2. 首次使用：准备与启动

### 电脑和手机需要准备什么

| 位置 | 要求 |
| --- | --- |
| 电脑系统 | Windows x64。 |
| WinFsp | 安装 [WinFsp](https://winfsp.dev/) 的运行组件；日常使用不需要 Developer 组件。 |
| ADB | 有可用的 `adb.exe`，手机已被识别。 |
| .NET | **仅依赖版需要** [.NET 10 x64 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。自包含版无需安装。 |
| 手机 | 开启 USB 调试，通过数据线连接，并允许此电脑调试。 |

### 启动步骤

1. **完整解压 ZIP** 到一个可写目录，例如 `D:/AndroidDrive`。不要在压缩包内直接运行。
2. **确认 ADB 连接正常**。在终端执行：

   ```powershell
   adb devices
   ```

   只有一台已授权手机时，应看到类似输出：

   ```text
   List of devices attached
   PHONE_A    device
   ```

3. **双击 `start.cmd`**。默认把手机的 `/sdcard` 挂载为 `K:`，并自动打开资源管理器。
4. **使用结束后双击 `stop.cmd`**。等卸载完成，再拔掉数据线。

启动后程序在后台运行，无需保持命令窗口打开。关闭资源管理器窗口不会卸载磁盘；请使用 `stop.cmd` 停止。

如果 ADB 未加入 PATH、K 盘已占用，或连接了多台手机，请先按下面的说明修改配置。

## 3. 修改配置

配置文件是解压目录中的 **`config.json`**。用记事本编辑并保存；如果已经挂载，先运行 `stop.cmd`，再运行 `start.cmd`，修改才会生效。

默认配置：

```json
{
  "Drive": "K:",
  "AdbPath": "",
  "Serial": "",
  "Root": "/sdcard"
}
```

| 配置项 | 含义 | 何时需要修改 |
| --- | --- | --- |
| `Drive` | Windows 盘符，默认 `K:`。 | 盘符已占用，或希望使用其他盘符。 |
| `AdbPath` | `adb.exe` 的完整路径。 | 自动查找不到 ADB 时填写。 |
| `Serial` | `adb devices` 中的设备序列号。 | 同时连接多台手机时，指定要挂载哪一台。 |
| `Root` | 手机上要展示的目录，默认 `/sdcard`。 | 只想展示某个可访问目录时修改。 |

**常见例子：ADB 放在 `E:/platform-tools/adb.exe`，希望挂载到 M 盘。**

```json
{
  "Drive": "M:",
  "AdbPath": "E:/platform-tools/adb.exe",
  "Serial": "",
  "Root": "/sdcard"
}
```

路径建议使用 `/`，这样可以直接填写。若使用 Windows 的反斜杠，JSON 中必须写成双反斜杠，例如 `E:\\platform-tools\\adb.exe`。

`AdbPath` 留空时，程序依次查找 PATH、`ANDROID_HOME`、`ANDROID_SDK_ROOT` 和默认 Android SDK 目录。`Serial` 留空时，仅在**恰好有一台已授权设备**的情况下自动选择。

## 4. 连接多台手机

### 只挂载其中一台

运行 `adb devices`，找到目标手机的序列号：

```text
List of devices attached
PHONE_A    device
PHONE_B    device
```

要挂载第一台，把 `config.json` 中的 `Serial` 改为 `"PHONE_A"`；挂载第二台则填 `"PHONE_B"`。请替换成实际序列号。

### 同时挂载两台

**把完整解压目录复制两份**，例如 `AndroidDrive-A` 和 `AndroidDrive-B`，按下表修改各自的 `config.json`：

| 所在目录 | `Serial` | `Drive` |
| --- | --- | --- |
| `AndroidDrive-A` | `PHONE_A` | `K:` |
| `AndroidDrive-B` | `PHONE_B` | `M:` |

分别双击两个目录中的 `start.cmd`，即可同时看到 K 盘和 M 盘。卸载时，也要分别运行各自目录中的 `stop.cmd`。

每份目录保存自己的进程记录、停止信号和日志，不能共用同一个运行目录。**两台手机不能使用同一盘符。**

## 5. 使用范围与限制

| 操作 | 是否支持 |
| --- | --- |
| 浏览目录、查看文件属性 | 支持。 |
| 将手机文件复制到电脑 | 支持。 |
| 用支持普通盘符的工具统计文件大小 | 支持，统计范围受 ADB 访问权限限制。 |
| 向手机复制文件、修改、删除或重命名 | **不支持，本项目为只读盘。** |
| 访问应用私有目录等受限位置 | 取决于手机的 ADB 权限，不保证可访问。 |
| 手机断线后自动恢复挂载 | 不支持；请停止挂载，重新连接后再启动。 |

还需要了解：

- 默认导出 `/sdcard`，即手机共享存储，不是手机完整系统分区。
- 目录元数据缓存约 3 秒；手机上刚发生的修改可能稍后显示。
- 顺序读取适合文件复制；大文件的随机跳转可能较慢，尤其是向后跳转。
- 仅连接本机默认 ADB 服务 `127.0.0.1:5037`，不支持自定义服务端口或远程 ADB 服务。
- 手机的 ADB 实现必须支持 `stat_v2` 和 `ls_v2`，目前没有旧协议回退。

## 6. 遇到问题先查这里

| 现象 | 处理方法 |
| --- | --- |
| 找不到 ADB | 在 `config.json` 的 `AdbPath` 中填写 `adb.exe` 完整路径。 |
| `adb devices` 没有设备 | 检查数据线、USB 调试和厂商 ADB 驱动。 |
| 设备状态是 `unauthorized` | 在手机上确认允许此电脑调试。 |
| 设备状态是 `offline` | 检查连接，重新插拔后确认状态恢复为 `device`。 |
| 提示有多台设备 | 在 `Serial` 中指定目标手机序列号。 |
| 提示盘符已占用 | 把 `Drive` 改为其他空闲盘符。 |
| 提示缺少 .NET | 安装 .NET 10 x64 Runtime，或改用自包含版。 |
| 提示缺少 WinFsp | 安装 WinFsp 后重试，自包含版也需要它。 |
| 提示不支持 `stat_v2` / `ls_v2` | 检查电脑和手机的 ADB 版本；旧实现可能无法使用。 |
| 某些目录无法打开 | 检查该目录是否允许 ADB 访问。 |
| 断线后磁盘不可用 | 运行 `stop.cmd`，重新连接手机，再运行 `start.cmd`。 |

其他错误请查看同目录的 **`stderr.log`** 和 **`stdout.log`**。反馈时可附错误内容；日志可能含设备序列号和路径，分享前请按需要脱敏。

## 性能参考

下面是同一台 Android 手机、同一 USB 连接下的两轮平均值。**这是单台设备上的实测，不代表所有手机。** AFS 是另一个需要手机端 APK 的文件盘方案。

| 测试 | 本项目 | AFS | 直接 `adb pull` |
| --- | ---: | ---: | ---: |
| 复制一个 107.7 MB 文件 | 2.76 秒 | 4.60 秒 | 2.80 秒 |
| 复制 256 个小文件，共约 9 MB | 1.66 秒 | 5.54 秒 | 1.59 秒 |
| 遍历 Pictures，5994 个文件，并获取大小 | 1.93 秒 | 2.06 秒 | — |
| 在同一大文件中随机读取 8 次，每次 64 KiB | 7.09 秒 | 0.137 秒 | — |

文件复制适合本项目；需要频繁跳转读取大文件时，带手机端服务、支持按偏移读取的方案更有优势。

测试未清空电脑及手机缓存，两轮执行顺序相反。复制文件的 SHA-256 和随机读取内容均验证通过；校验、清理不计入耗时。本项目只读，未测试写入性能。

## 源码编译

普通用户只需下载发布包。以下内容供需要修改源码或自行打包的开发者使用。

安装 **.NET 10 SDK** 和带 **Developer 组件的 WinFsp**，在项目根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

WinFsp 安装在其他目录时：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -WinFspDir 'D:/Tools/WinFsp'
```

构建完成后，`dist` 目录包含两个 ZIP 和校验文件：

```text
AndroidAdbFs-windows-x64-framework-dependent.zip
AndroidAdbFs-windows-x64-self-contained.zip
SHA256SUMS.txt
```

自包含版未启用裁剪，以保留 WinFsp 托管绑定所需功能。两个版本都不是 Native AOT。

如需直接运行程序，在**解压后的发布包目录**执行：

```powershell
./app/AndroidAdbFs.exe --adb 'E:/platform-tools/adb.exe' --mount M: --root /sdcard --serial PHONE_A
```

前台运行时按 Ctrl+C 正常卸载；启动脚本在后台运行时，通过 `--stop-file` 配合停止脚本卸载。

## 实现与许可

数据访问路径：

```text
Windows 程序 → WinFsp → 本项目 → 本机 ADB 服务 → USB → 手机 adbd
```

目录读取使用 ADB Sync v2，批量获取目录项与属性；文件数据通过 Sync `RECV` 流式读取。完整事务结束后的连接可复用，中途停止的读取连接会被丢弃。程序没有额外的整文件内容缓存；向后跳转可能需要重新读取并跳过前面的数据。

本项目使用 **GPL-3.0-or-later**，见 [LICENSE](LICENSE)。

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos。

WinFsp 仓库：<https://github.com/winfsp/winfsp>。其许可与例外条款保留在 [third_party/WinFsp-License.txt](third_party/WinFsp-License.txt)，发布包亦附带该文件。
