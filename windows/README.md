# WeChatBridge Windows 版（开发中）

这是 WeChatBridge 的 Windows 独立实现，使用 WPF + .NET 8。它不读取微信数据库、不注入微信进程，也不依赖 macOS Share Extension。

当前版本提供三条可验证链路：

1. 从微信“转发到其他应用”接收 ZIP；
2. 也可以选择或拖入微信导出的 ZIP / 文件夹；
3. 在本地收件箱保存原始来源；
4. 校验归档路径和展开大小，避免 ZIP 路径穿越与压缩炸弹；
5. 导入到一个已共享给 Integration 的 Notion 页面：每个归档创建一个子页面，写入标题、发送人、时间和原文，并回读验证；
6. 或生成 Obsidian Markdown 笔记，并保留原始归档和附件。

收到归档后，点击“发送到 Codex 分析”会在本地收件箱创建独立工作区，生成 `CHAT.md`，并以只读模式调用本机 `codex.exe exec`。分析结果保存为该工作区中的 `CODEX_ANALYSIS.md`；点击“打开 Codex 工作区”可以在 Codex 桌面应用中继续追问。这个按钮是显式操作，聊天内容不会因为收到微信分享而自动提交给 Codex。

## 从微信直接接收

普通 `.exe` 不会出现在微信的“选择电脑中的应用”列表中。需要先构建并安装带包身份的 MSIX：

```powershell
pwsh -File windows\Package\Build-WindowsPackage.ps1
```

安装签名后的 MSIX 后，重新打开微信，在合并转发聊天记录的“转发到其他应用”中选择 `WeChatBridge`。程序会在分享操作结束前将文件复制到 `%LOCALAPPDATA%\WeChatBridge\Inbox`，然后在窗口中列为待处理来源。详细签名说明见 [windows/Package/README.md](Package/README.md)。

## 导入 Notion

1. 在 Notion 创建 Internal Integration，复制 Token。
2. 打开目标父页面，通过页面菜单的 Connections 将该 Integration 分享给页面。
3. 启动应用，在“Notion（推荐）”标签中粘贴目标页面链接或 ID，再粘贴 Token。
4. 选择微信合并转发得到的 ZIP，点击“导入到 Notion”。Token 会使用当前 Windows 用户的 DPAPI 加密保存在 `%LOCALAPPDATA%\WeChatBridge\notion.config.json`。

程序使用 Notion REST API 创建子页面、追加块，并读取页面块列表确认本次导入标记存在。当前版本导入文字内容；原始 ZIP 和附件仍保存在本机收件箱，尚未上传到 Notion 文件存储。

目标页面必须已分享给这个 Integration，并且 Integration 具备读取和插入内容的能力。页面链接中的页面 ID 可以直接粘贴，程序会提取并校验 ID。

## 发送到 Codex

点击“发送到 Codex 分析”前，请确认你希望把本次聊天内容交给当前登录的 Codex。程序只给 Codex 当前批次工作区的读取权限，聊天原文被标记为不可信资料，避免把聊天中夹带的提示词当成执行指令。Codex 的分析权限、登录状态和数据处理仍由本机 Codex 配置决定。

## 构建

需要 Windows、Visual Studio 2022（含 .NET 桌面开发）或 .NET 8 SDK：

```powershell
dotnet restore windows\WeChatBridge.Windows.sln
dotnet test windows\WeChatBridge.Windows.sln --configuration Release
dotnet run --project windows\WeChatBridge.Windows\WeChatBridge.Windows.csproj
```

发布单文件目录（仅适合直接运行，不会注册微信分享目标）：

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

Windows 端不读取或修改微信内部数据库，也不注入微信进程。微信直收使用 Windows Share Target/MSIX 包身份；自动导入 Notion、目标应用管理和托盘常驻仍属于后续 Windows 专用适配。
