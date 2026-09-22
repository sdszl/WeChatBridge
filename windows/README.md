# WeChatBridge Windows 版（开发中）

这是 WeChatBridge 的 Windows 独立实现，使用 WPF + .NET 8。它不读取微信数据库、不注入微信进程，也不依赖 macOS Share Extension。

当前版本先完成一条可验证的本地链路：

1. 选择或拖入微信导出的 ZIP / 文件夹；
2. 在本地收件箱保存原始来源；
3. 校验归档路径和展开大小，避免 ZIP 路径穿越与压缩炸弹；
4. 生成 Obsidian Markdown 笔记，并保留原始归档和附件。

## 构建

需要 Windows、Visual Studio 2022（含 .NET 桌面开发）或 .NET 8 SDK：

```powershell
dotnet restore windows\WeChatBridge.Windows.sln
dotnet test windows\WeChatBridge.Windows.sln --configuration Release
dotnet run --project windows\WeChatBridge.Windows\WeChatBridge.Windows.csproj
```

发布单文件目录：

```powershell
dotnet publish windows\WeChatBridge.Windows\WeChatBridge.Windows.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

输出在 `windows\WeChatBridge.Windows\bin\Release\net8.0-windows\win-x64\publish`。

## 与 macOS 版的边界

Windows 端目前不模拟 macOS 的 Share Extension。自动粘贴、目标应用管理、托盘常驻和安装包属于后续 Windows 专用适配，不会通过读取或修改微信内部数据实现。
