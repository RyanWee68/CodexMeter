# CodexMeter（Codex 用量条）

Windows 10/11 任务栏左侧的透明 Codex 用量小工具，集中展示五小时额度、每周额度和重置卡信息。使用 C# / WinForms / .NET 8，无第三方 NuGet 依赖。

## 效果预览

![Codex 用量条：五小时额度、每周额度、重置卡到期日期及刷新状态](docs/images/codexmeter-preview.png)

当前版本实际绘制的透明面板，使用示例数据展示；运行时显示当前账号的实时用量。图片为两倍尺寸，便于查看细节。

## 功能与操作

| 内容 | 展示信息 |
| --- | --- |
| 五小时额度 | 可用百分比、进度条、距重置时间 |
| 每周额度 | 可用百分比、进度条、距重置时间 |
| 重置卡 | 可用次数、最早到期日期 |
| 刷新状态 | 上次刷新时间、刷新按钮、失败提示 |

- 透明背景，紧凑三列布局，支持 Windows DPI 缩放。
- 每五分钟自动刷新；单击面板立即刷新；双击打开设置。
- 右键面板或通知区图标，可刷新、打开设置或退出。
- 首次正常运行即启用当前用户开机启动，设置中可随时关闭。
- 自动跟随本机官方 Codex CLI 账号，不提供独立账号切换。
- 本工具只读取和展示用量，不会使用重置卡。

## 运行

### 环境要求

- Windows 10/11，使用 Explorer 任务栏。
- .NET 8 Desktop Runtime；从源码构建还需 .NET 8 或兼容 SDK。
- 官方 Codex CLI（`codex.exe`），以及能够访问 ChatGPT 的网络。
- 已登录的 ChatGPT 订阅账号，或首次运行后通过设置完成授权。

### 从源码构建并启动

```powershell
git clone --branch mian https://github.com/RyanWee68/CodexMeter.git
cd CodexMeter
dotnet publish ./CodexMeter.csproj -c Release --no-self-contained -o dist
Start-Process ./dist/CodexMeter.exe
```

仓库保存源码，不包含 `dist/` 可执行文件。开发时也可以使用 `dotnet run`。

### 开机启动

构建后，在项目目录执行：

```powershell
./Install.ps1
```

直接运行 `dist/CodexMeter.exe` 会在当前用户的“启动”文件夹创建快捷方式；运行 `Install.ps1` 会启动工具、启用开机启动，并迁移旧版设置。保持 dist 目录位置不变。双击面板打开设置，可以关闭开机启动；右键面板或通知区图标可退出。

如需卸载，先在设置中关闭开机启动，再退出程序并删除程序目录。需要清理设置和日期缓存时，可自行删除 `%LOCALAPPDATA%/CodexMeter`。

从旧版 CodexMonitor 更新时，安装脚本会复制已有设置到新目录（保留旧文件），并将开机启动项替换为 CodexMeter。

## 登录与刷新

- 为了区分双击，单击刷新会等待系统设置的双击间隔。
- 自动跟随官方 CLI 使用的本机 Codex 账号，包括共享同一 Codex 登录目录的桌面客户端。
- 没有订阅登录时，双击进入设置，点击“浏览器授权登录”。授权由官方 CLI 处理，会更新其共享的本机登录状态。本工具不另存令牌，明细补查时仅在内存中使用 CLI 已保存的本机授权。
- CLI 自动查找 exe 同目录、npm 全局安装目录和 PATH；找不到时可在设置中选择官方 `codex.exe`。
- 支持 DPI 缩放，任务栏位置定时校正；全屏应用时隐藏，任务栏自动隐藏时随任务栏隐藏。通知区图标可随时打开设置。

## 任务栏限制

Windows 11 没有公开的自定义 deskband 宿主接口。本版本是在主任务栏左侧叠加无边框面板，不会为它挪动 Windows 按钮。建议将任务栏图标居中并关闭左侧小组件；图标靠左时可能遮挡开始菜单按钮。竖向任务栏会显示在其右侧。没有安装 Explorer 扩展，也不修改 Windows 任务栏设置。

自动检测的是官方 Codex CLI 的登录状态；不提取 ChatGPT Windows 应用的私有登录数据库。如果只有 ChatGPT 应用登录，需要通过设置授权一次。

重置卡接口未提供时显示“未提供”，没有到期详情时显示“到期未知”。多张卡时显示已返回详情中最早的可用卡到期日期；官方可能只返回部分卡详情。倒计时归零显示“待刷新”，不会推断额度已恢复。本工具只展示重置卡，不会消耗卡。

当接口只返回卡数量、明细缺失时，先更新已获取的用量，再等待一秒补查一次（最多二十五秒，整个刷新不超过四十秒，不影响已成功读取的用量）。同一账号且卡数量不变时，可沿用一小时内确认过、尚未过期的日期，以 `*` 和悬停提示标明历史值。数量改变、账号改变、完整明细明确不含日期或日期已过时失效。确认过的记录保存在 `%LOCALAPPDATA%/CodexMeter/reset-expiry.json`，重启后仍可沿用；仅保存账号摘要、次数、日期及确认时间，不保存邮箱或授权信息。没有邮箱可识别账号时不沿用缓存。零张卡显示“无可用卡”。

## 构建和验证

```powershell
dotnet build -c Release
dotnet publish -c Release --no-self-contained -o dist
Start-Process ./dist/CodexMeter.exe -ArgumentList '--self-test' -Wait
Start-Process ./dist/CodexMeter.exe -ArgumentList '--probe' -Wait
Start-Process ./dist/CodexMeter.exe -ArgumentList '--preview' -Wait
# 在当前桌面临时显示 50 秒，保存自身面板及位置检查结果后退出
Start-Process ./dist/CodexMeter.exe -ArgumentList '--smoke' -Wait
```

验证产物写入当前目录的 `artifacts/`。probe 只导出衍生用量，不包含邮箱、账号 ID 或令牌。受限沙箱可能阻止官方 CLI 初始化自己的数据库，应在普通用户权限下运行。

`--self-test` 执行解析与缓存自检；`--probe` 需要真实账号及网络；`--preview` 使用示例数据生成透明面板图片；`--smoke` 需要先退出正在运行的实例，否则单实例保护会使它直接退出。

## 项目结构

| 文件 | 职责 |
| --- | --- |
| `MonitorForm.cs` | 面板绘制、交互、刷新调度和设置窗口 |
| `Taskbar.cs` | 任务栏定位、缩放与隐藏处理 |
| `CodexClient.cs` | 官方 CLI App Server 通信与登录 |
| `Usage.cs` | 用量、倒计时和重置卡数据解析 |
| `ResetCreditDetails.cs` | 重置卡明细补查与账号核对 |
| `ResetExpiryCache.cs` | 到期日期缓存及失效规则 |
| `Settings.cs` | 本地设置与开机启动 |
| `SelfTest.cs` | 无网络自检 |
| `Program.cs` | 程序入口及诊断命令 |
| `Install.ps1` | 启动、开机启动注册及旧版设置迁移 |

## 数据存储与接口限制

设置保存在 `%LOCALAPPDATA%/CodexMeter/settings.json`；开机启动使用当前用户“启动”文件夹中的 `CodexMeter.lnk`。旧版注册表 `Run` 启动项会在更新后移除。没有计划任务或后台服务。

登录和用量协议：[OpenAI Codex App Server 文档](https://learn.chatgpt.com/docs/app-server)，使用 `account/read`、`account/rateLimits/read` 及官方浏览器授权。CLI 对重置卡明细有五秒内部超时；明细缺失时，仅向固定的 ChatGPT 官方明细地址补查，最多等待二十五秒，并核对当前账号邮箱及接口返回的账号 ID（如有）。临时读取 CLI 已保存的授权信息，仅在内存中使用，不另存、不记录、不跟随 HTTP 重定向。此补查地址属于官方客户端内部接口，变更或失败时回退到已验证日期缓存。
