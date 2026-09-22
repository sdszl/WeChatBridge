# Windows Share Target 包

这个目录把 WPF 程序打成 MSIX。安装后，Windows 会登记 `windows.shareTarget`，微信“转发到其他应用”中的“选择电脑中的应用”才有机会显示 `WeChatBridge`。

## 构建

在仓库根目录执行：

```powershell
pwsh -File windows\Package\Build-WindowsPackage.ps1
```

脚本默认生成依赖 .NET 8 Desktop Runtime 的 `windows\artifacts\windows-package\WeChatBridge.msix`。如果目标电脑没有 .NET 8 Desktop Runtime，可以追加 `-SelfContained`，生成自带运行时的包。

这一步只生成未签名包。Windows 不会安装未签名的 MSIX；实际安装前需要使用发布证书签名，例如：

```powershell
pwsh -File windows\Package\Build-WindowsPackage.ps1 `
  -CertificatePath C:\path\WeChatBridge.pfx `
  -CertificatePassword "<证书密码>"
```

本地测试时，证书的 Subject/Publisher 必须与 `AppxManifest.xml` 中的 `CN=WeChatBridge Development` 一致，并将对应 `.cer` 导入当前用户的“受信任的人”证书存储。不要把 `.pfx`、私钥或真实密码提交到仓库。

## 安装和测试

安装已签名的 MSIX 后，完全退出并重新打开微信，再从合并转发聊天记录进入“转发到其他应用”。选择文件时应看到 `WeChatBridge`。

WeChatBridge 收到 ZIP 后会先复制到 `%LOCALAPPDATA%\WeChatBridge\Inbox`，再在窗口中显示为待处理来源。它不会读取或修改微信数据库，也不会把 Notion Token 写入项目目录。窗口中的“发送到 Codex 分析”是显式操作：程序会用只读模式启动本机 `codex.exe exec`，把当前工作区交给 Codex，并将结果保存为 `CODEX_ANALYSIS.md`。

当前包只声明 `.zip` + `StorageItems`，避免在微信分享菜单中对不支持的文件类型乱显示。收到 ZIP 后，Notion 和 Obsidian 仍使用现有的安全校验与回读流程。
