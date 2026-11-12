# Android ADB WinFsp 文件盘

将 Android 手机的共享存储挂载为 Windows **只读磁盘**。挂载后，可以用资源管理器浏览文件、复制到电脑，也可以用支持普通盘符的工具统计目录大小。

**下载一个 EXE，双击即可选择手机和盘符。无需解压目录，不用 CMD、PowerShell 脚本或配置文件。** v0.3.0-rc.1 试用版内嵌官方 WinFsp 安装包，缺少时可通过 UAC 安装；手机不用 APK、不要求 root，电脑仍需准备 ADB。

## 1. 选择下载版本

前往 **[WinFsp 集成试用版下载页](https://github.com/dadaewqq/android-adb-winfsp/releases/tag/v0.3.0-rc.1)**，在 **Assets** 中下载以下任意一个 EXE。此版尚未在干净电脑完成首次安装实测；不需要集成安装包时可使用 [v0.2.0](https://github.com/dadaewqq/android-adb-winfsp/releases/tag/v0.2.0)。

| 电脑环境 | 下载文件 | 文件大小 |
| --- | --- | ---: |
| 已安装 .NET 10 x64 Runtime | `AndroidAdbFs-windows-x64-framework-dependent.exe` | 约 2.6 MiB |
| 没装 .NET，或不想额外安装 | `AndroidAdbFs-windows-x64-self-contained.exe` | 约 37.3 MiB |

- **依赖版**：不带 .NET，使用电脑上安装的 .NET 10 x64 Runtime。
- **自包含版**：自带 .NET 运行时，不需要另外安装 .NET。
- 两版功能相同，都内嵌 **WinFsp 2.1 官方安装包**；它们均不是 Native AOT。

只需下载一个 EXE。页面上的 `Source code` 是源码，`SHA256SUMS.txt` 用于校验，普通使用无需下载它们。

## 2. 准备环境

| 位置 | 需要准备什么 |
| --- | --- |
| 电脑系统 | Windows x64。 |
| WinFsp | 已安装则直接使用；未安装时程序询问是否安装内嵌的官方运行组件，需要在 UAC 中允许管理员权限。 |
| ADB | 有可用的 `adb.exe`，手机能被识别。程序会自动查找，也可在窗口中输入完整路径。 |
| .NET | **只有依赖版需要** [.NET 10 x64 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。 |
| 手机 | 开启 USB 调试，通过数据线连接，并允许此电脑调试。 |

EXE 不包含 ADB。WinFsp 安装包已内嵌，无需另行下载；它包含系统驱动，首次仍需要安装到 Windows。可访问哪些目录，取决于手机的 ADB 权限。

## 3. 双击、选择、使用

1. **连接手机**，在手机上允许 USB 调试。
2. **双击下载的 EXE**。如果缺少 WinFsp，输入 `Y` 同意安装，并在 UAC 中允许；已安装时会自动跳过。随后如果未找到 ADB，按提示输入 `adb.exe` 完整路径，也可以把文件拖入窗口后回车。
3. **选择手机**：窗口会列出已授权设备，输入编号。只有一台时，直接回车即可。
4. **选择盘符**：直接回车采用建议的空闲盘符，或输入指定盘符，例如 `M`。已占用的盘符会提示重新选择。
5. 看到 **“已挂载到……”** 后，在资源管理器的“此电脑”中打开对应磁盘。
6. **使用结束后，回到程序窗口按回车，或按 Ctrl+C**，正常卸载并退出，再拔掉数据线。

**使用过程中请保留程序窗口。** 关闭资源管理器不会卸载磁盘；请通过程序窗口正常卸载。

程序默认展示手机 `/sdcard`，即共享存储。无需编辑配置文件，不会在 EXE 旁创建脚本、日志或配置目录。

### 第一次安装 WinFsp

- 内嵌的是未修改的官方 `winfsp-2.1.25156.msi`，构建时验证 SHA-256 和数字签名，释放时再次验证 SHA-256。
- 输入 `Y` 后，仅 Windows Installer 通过 UAC 提权；文件盘主程序保持原权限。没有管理员授权时无法安装驱动。
- 只安装运行组件，不安装 Developer 组件；不自动升级已有 WinFsp，也不自动重启。
- Windows Installer 要求重启时，程序提示手动重启并停止挂载；重启后再次运行即可。
- 安装日志保存在 `%LOCALAPPDATA%/AndroidAdbFs/Setup/` 下；安装结束后删除临时 MSI，保留日志以便排查。Windows Installer 自身可能保留系统维护所需的安装缓存。
- 新电脑首次安装仍需实际验证。本机已验证已有安装时的跳过流程；缺失检测和用户取消已在进程内隔离的注册表测试中验证，未卸载本机驱动测试全新安装。

### 单文件是否会在运行时释放文件？

会。.NET 单文件程序运行时自动使用临时缓存目录，通常位于 `%TEMP%/.net/AndroidAdbFs/`，用于加载打包的组件。分发给同事时只需一个 EXE，无需手动维护缓存内容。首次启动可能稍慢。

## 4. 连接多台手机

**只挂载一台**：在设备列表中选择对应编号即可，不需要填写序列号配置。

**同时挂载多台**：再次双击同一个 EXE，打开第二个程序窗口，选择另一台手机和另一个空闲盘符。每个窗口管理自己挂载的磁盘，互不共用停止信号文件。

例如：

| 程序窗口 | 选择的手机 | 选择的盘符 |
| --- | --- | --- |
| 第一个窗口 | 手机 A | `K:` |
| 第二个窗口 | 手机 B | `M:` |

使用结束后，分别在两个窗口按回车卸载。无需复制 EXE，也无需复制目录。

## 5. 使用范围与限制

| 操作 | 是否支持 |
| --- | --- |
| 浏览目录、查看属性、复制文件到电脑 | 支持。 |
| 用支持普通盘符的工具统计文件大小 | 支持，统计范围受 ADB 权限限制。 |
| 向手机写入、修改、删除或重命名 | **不支持，本项目为只读盘。** |
| 访问应用私有目录等受限位置 | 取决于手机的 ADB 权限，不保证可访问。 |
| 断线后自动恢复挂载 | 不支持。请正常退出，重新连接后再次运行。 |

其他限制：

- 共享存储不等于手机完整系统分区。
- 目录元数据缓存约 3 秒，手机上刚发生的修改可能稍后显示。
- 适合顺序读取和文件复制；大文件的随机跳转可能较慢，尤其是向后跳转。
- 仅支持本机默认 ADB 服务 `127.0.0.1:5037`，不支持远程服务或自定义服务端口。
- 手机的 ADB 实现必须支持 `stat_v2` 和 `ls_v2`，目前没有旧协议回退。

## 6. 常见问题

| 现象 | 处理方法 |
| --- | --- |
| 找不到 ADB | 在窗口提示时输入 `adb.exe` 完整路径；也可以把 ADB 加入 PATH。 |
| 未找到已授权手机 | 检查数据线、USB 调试、厂商 ADB 驱动和手机上的授权提示，然后回车刷新。 |
| 状态是 `unauthorized` | 在手机上确认允许此电脑调试。 |
| 状态是 `offline` | 检查连接，重新插拔后重试。 |
| 连接了多台手机 | 在设备列表中输入目标手机的编号。 |
| 盘符已占用 | 输入其他盘符；直接回车会使用建议的空闲盘符。 |
| 提示缺少 .NET | 安装 .NET 10 x64 Runtime，或改用自包含 EXE。 |
| 提示缺少 WinFsp、驱动或 DLL | 双击程序并同意安装内嵌 WinFsp；无法授权时联系管理员。已安装但文件损坏时可手动修复 WinFsp。 |
| WinFsp 安装失败 | 根据窗口显示的 MSI 退出码及安装日志排查；取消 UAC 或安装时程序不会继续挂载。 |
| WinFsp 提示需要重启 | 保存工作，手动重启电脑后再次运行。 |
| 提示不支持 `stat_v2` / `ls_v2` | 检查电脑和手机的 ADB 版本，旧实现可能无法使用。 |
| 某些目录无法打开 | 检查该目录是否允许 ADB 访问。 |
| 断线后磁盘不可用 | 在程序窗口正常退出，重新连接手机后再次运行。 |

错误会直接显示在程序窗口中。反馈前请按需要隐藏设备序列号和文件路径。

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

## 源码编译与命令行

普通用户只需下载 EXE。自行编译需要 **.NET 10 SDK** 和带 **Developer 组件的 WinFsp**。

在项目根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
```

WinFsp 安装在其他目录时：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -WinFspDir 'D:/Tools/WinFsp'
```

输出位于 `dist`：

```text
AndroidAdbFs-windows-x64-framework-dependent.exe
AndroidAdbFs-windows-x64-self-contained.exe
SHA256SUMS.txt
```

自包含版启用单文件压缩；两版均未启用程序集裁剪，以保留 WinFsp 托管绑定所需功能。WinFsp 托管绑定自动释放到运行时缓存目录；系统驱动通过内嵌 MSI 安装，不是便携加载。

构建前脚本会校验 `vendor/winfsp-2.1.25156.msi`，不存在时通过系统代理下载固定的官方版本，拒绝直接下载。也可以自行放入哈希匹配的官方 MSI。独立下载命令：

```powershell
./tools/fetch-winfsp.ps1 -ProxyUrl 'http://127.0.0.1:5904'
```

安装包来源及哈希见 [third_party/WinFsp-Installer.md](third_party/WinFsp-Installer.md)。测试检测、校验和取消逻辑（不会安装驱动）：

```powershell
dotnet run --project ./tests/BootstrapTests.csproj -c Release
```

无参数运行时进入交互选择模式。如需固定参数，可从命令行执行：

```powershell
./AndroidAdbFs-windows-x64-self-contained.exe --adb 'E:/platform-tools/adb.exe' --mount M: --root /sdcard --serial PHONE_A
```

命令行模式下按 Ctrl+C 正常卸载。高级自动化可用 `--stop-file PATH` 指定停止信号文件；交互模式不需要此文件。`--help` 查看参数，`--license` 显示内嵌的项目与 WinFsp 许可。

`--check-winfsp` 只检测安装状态；`--install-winfsp` 在缺少时直接请求 UAC 安装，已有时跳过；`--extract-winfsp PATH` 仅释放校验过的官方 MSI，不执行安装，也不覆盖已有文件。

ADB 自动查找顺序：EXE 所在目录、PATH、`ANDROID_HOME`、`ANDROID_SDK_ROOT`、默认 Android SDK 目录。找不到时，交互模式提示输入路径。

## 实现与许可

```text
Windows 程序 → WinFsp → 本项目 → 本机 ADB 服务 → USB → 手机 adbd
```

目录读取使用 ADB Sync v2，批量获取目录项与属性；文件数据通过 Sync `RECV` 流式读取。完整事务结束后的连接可复用，中途停止的读取连接会被丢弃。程序没有额外的整文件内容缓存，向后跳转可能需要重新读取并跳过前面的数据。

本项目使用 **GPL-3.0-or-later**，见 [LICENSE](LICENSE)。

WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos。

WinFsp 仓库：<https://github.com/winfsp/winfsp>。其许可与例外条款保留在 [third_party/WinFsp-License.txt](third_party/WinFsp-License.txt)。两份许可均已内嵌到 EXE，可通过 `--license` 查看。

旧版脚本分发包仍可从 [v0.1.1](https://github.com/dadaewqq/android-adb-winfsp/releases/tag/v0.1.1) 下载。
