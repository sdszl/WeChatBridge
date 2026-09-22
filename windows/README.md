# WeChatBridge Windows 版（开发中）

这是 WeChatBridge 的 Windows 独立实现，使用 WPF + .NET 8。它不读取微信数据库、不注入微信进程，也不依赖 macOS Share Extension。

当前版本提供两条可验证链路：

1. 选择或拖入微信导出的 ZIP / 文件夹；
2. 在本地收件箱保存原始来源；
3. 校验归档路径和展开大小，避免 ZIP 路径穿越与压缩炸弹；
4. 导入到一个已共享给 Integration 的 Notion 页面：每个归档创建一个子页面，写入标题、发送人、时间和原文，并回读验证；
5. 或生成 Obsidian Markdown 笔记，并保留原始归档和附件。

## 导入 Notion

1. 在 Notion 创建 Internal Integration，复制 Token。
2. 打开目标父页面，通过页面菜单的 Connections 将该 Integration 分享给页面。
3. 启动应用，在“Notion（推荐）”标签中粘贴目标页面链接或 ID，再粘贴 Token。
4. 选择微信合并转发得到的 ZIP，点击“导入到 Notion”。Token 会使用当前 Windows 用户的 DPAPI 加密保存在 `%LOCALAPPDATA%\WeChatBridge\notion.config.json`。

程序使用 Notion REST API 创建子页面、追加块，并读取页面块列表确认本次导入标记存在。当前版本导入文字内容；原始 ZIP 和附件仍保存在本机收件箱，尚未上传到 Notion 文件存储。

目标页面必须已分享给这个 Integration，并且 Integration 具备读取和插入内容的能力。页面链接中的页面 ID 可以直接粘贴，程序会提取并校验 ID。

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
