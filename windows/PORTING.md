# Windows port

The Windows implementation is deliberately a separate application from the
macOS SwiftUI app. The reusable product boundary is the local archive and
Markdown workflow; OS-specific integrations are added behind Windows APIs.

## Current vertical slice

- choose or drag in a WeChat-exported ZIP or folder;
- validate ZIP paths, entry counts, expanded size, UTF-8 transcript size, and
  reparse points before opening files;
- preserve a local copy under `%LOCALAPPDATA%\\WeChatBridge\\Inbox`;
- create an Obsidian-compatible Markdown note and preserve the original
  archive plus extracted attachments;
- record success or failure in a local JSON manifest.

## Next Windows-specific slices

1. clipboard file/text payloads;
2. foreground activation and paste through Windows UI Automation / SendInput;
3. configurable target applications and a tray application;
4. Windows packaging and signed distribution.
