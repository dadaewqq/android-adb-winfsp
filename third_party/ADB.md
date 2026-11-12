# 内置 ADB 37.0.1

取自 Google 官方稳定版 Android SDK Platform-Tools 37.0.1（Windows）：

- 官方版本元数据：<https://dl.google.com/android/repository/repository2-3.xml>，`platform-tools`、`channel-0`。
- 固定下载地址：<https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip>。
- 官方元数据中的 ZIP SHA-1：`e03e78b1d80b396f1c3358e31251cb31740e1110`。
- 本项目固定的 ZIP SHA-256：`45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99`。
- `adb version`：`37.0.1-15733141`，ADB 协议版本 `1.0.41`。

只分发原样的 `adb.exe`、`AdbWinApi.dll`、`AdbWinUsbApi.dll` 及完整 `NOTICE.txt`，不包含 fastboot 等无关工具。这些文件各自的 SHA-256 固定在 `src/AdbTools.cs`，首次释放及后续使用前校验。

ADB 属于 Android 开源组件。Google SDK 许可第 3.5 条说明，SDK 中开源组件的使用、复制与分发适用各自的开源许可。原包的完整开源许可与声明 `NOTICE.txt` 同时内嵌到 EXE 并释放到 ADB 缓存目录，可通过本程序 `--license` 查看。

ADB 源码及其许可：<https://android.googlesource.com/platform/packages/modules/adb/>；Windows USB 组件源码：<https://android.googlesource.com/platform/development/+/refs/heads/main/host/windows/usb/>。

运行时缓存位于 `%LOCALAPPDATA%/AndroidAdbFs/Tools/adb-37.0.1/`，不修改系统 PATH。默认复用本机 `5037` 端口上的兼容 ADB 服务；程序不主动调用 `kill-server`，卸载文件盘时也不会停止共享服务。首次使用仍可能需要手机 USB 驱动，并需要在手机确认 USB 调试授权。
