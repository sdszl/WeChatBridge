# 本地配置

这是 WeChatBridge 仓库中账号、Token 和机器本地设置的唯一说明入口。真实凭据不进入源码、测试、日志、Markdown 或 Git 提交。

## Windows Notion 导入

| 用途 | 使用者 | 本机文件 | 字段 |
| --- | --- | --- | --- |
| 将微信归档导入 Notion | `WeChatBridge.Windows` | `%LOCALAPPDATA%\WeChatBridge\notion.config.json` | `parentPageId`、由 Windows DPAPI 加密后的 `protectedToken` |

程序首次导入时在界面中填写目标页面和 Integration Token，并把 Token 绑定到当前 Windows 用户和当前机器。Token 不会保存到仓库，也不会出现在命令行参数、异常信息或日志中。

示例文件见 [windows/notion.config.example.json](windows/notion.config.example.json)。它只包含占位符，不能直接作为真实配置使用；请在应用界面中保存 Token。

## 设置方式

1. 在 Notion 创建 Internal Integration，复制它的 Token。
2. 打开准备接收导入内容的 Notion 页面，在页面菜单的 Connections 中把该 Integration 加入页面。
3. 启动 Windows 版，在“目标页面”粘贴页面链接或页面 ID，在 Token 框粘贴 Token，然后点击“导入到 Notion”。

如果需要撤销访问，请在 Notion 中移除该 Integration 的页面连接，或删除本机的 `%LOCALAPPDATA%\WeChatBridge\notion.config.json` 后重新配置。
