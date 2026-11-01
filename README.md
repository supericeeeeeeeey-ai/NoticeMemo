# NoticeMemo

> 把学院群里的通知，自动收进一张张可以行动的卡片。

NoticeMemo 是一个本地优先的 Windows 通知备忘录，针对“学院通知群消息很多，截止时间容易漏掉”的场景。复制 QQ 群里的通知后，程序会在后台自动识别标题、截止时间、地点、分类、标签和链接，并保存成可搜索、可勾选的备忘录。

![NoticeMemo 主界面](docs/preview.png)

## v0.2.0

这一版从“手动点按钮导入”升级为可长期运行的后台捕获工具：

- 后台监听 Windows 剪贴板，复制通知后无需切回应用
- 用标题、日期、行动词、链接等信号过滤普通聊天和代码片段
- 高置信度通知自动收录；其余候选进入托盘待确认队列
- 系统托盘常驻，关闭主窗口后继续运行
- 可选随 Windows 登录自动后台启动
- 单实例运行，重复双击会唤醒已有窗口
- 设置持久保存，可暂停捕获、关闭提示或修改来源群聊
- 保留 v0.1 的手动导入、搜索筛选、完成状态、SQLite 存储和 Markdown 导出

![后台捕获设置](docs/preview-settings.png)

## 快速开始

1. 从 GitHub Releases 下载 `NoticeMemo.exe`，或运行本地发布产物 `outputs\NoticeMemo-win-x64\NoticeMemo.exe`。
2. 双击启动。程序目前未做代码签名，Windows SmartScreen 可能提示“未知发布者”，请核对文件来源后选择“更多信息 → 仍要运行”。
3. 在 QQ 学院通知群里选中一条通知并复制。
4. NoticeMemo 会在后台判断内容。高置信度通知直接进入“待整理”，并在右下角显示托盘提示。
5. 关闭主窗口只会隐藏到托盘。需要完全退出时，使用托盘菜单中的“退出 NoticeMemo”。

第一次使用建议打开“后台捕获设置”，确认来源群聊、自动收录策略和开机启动选项。

## 全天候捕获：能做到什么

当前版本能实现的是“后台剪贴板捕获”：

- 登录 Windows 后，程序可以常驻托盘并持续运行
- 睡眠/唤醒后继续监听，无需重新启动
- 在 QQ 中复制通知的瞬间自动识别并去重
- 高置信度内容自动入库，较低置信度内容排队等待确认
- 开启“随 Windows 登录后自动启动”后，重启电脑也会回到后台

它**不能**在没有人查看 QQ 的情况下读取新消息。完整链路仍然是：

```text
QQ 收到消息 → 群成员查看并复制 → Windows 剪贴板 → NoticeMemo 自动识别
```

如果 QQ 没有运行、通知没有被查看和复制，或者程序在托盘菜单中被完全退出，捕获都不会发生。

## 为什么不直接监听 QQ

直接读取普通 QQ 群的全部消息，需要越过应用边界，当前版本有意不这么做：

- 该群不是使用者创建或管理的群，读取实时消息或历史记录需要群主/管理员的明确授权。
- 第三方 QQ 协议、注入客户端、读取本地聊天数据库或模拟点击存在账号风控、隐私泄露和违反平台规则的风险。
- QQ 官方机器人是合规方向，但机器人能否进群、能收到哪些群事件，取决于平台开放能力和群管理员的授权设置；普通成员不能自行把机器人变成“全群监听器”。
- 即使接入官方机器人，也不等于可以稳定获得群内所有消息，仍应以实际权限和平台规则为准。

因此当前版本只处理已经进入 Windows 剪贴板的文本，不读取 QQ 窗口、聊天数据库或网络协议。更完整的路线比较见 [QQ 监听边界说明](docs/qq-listening.md)。

## 自动收录规则

后台捕获不是“复制什么就存什么”。过滤器会综合判断：

- `【...】` 群公告标题、`@全体成员`、首行“通知/公告/安排”等结构
- 能否解析出中文日期、相对日期或截止时间
- 是否包含“报名、截止、提交、地点、会议、讲座、考试”等行动词
- 是否有多行正文、链接、称呼等通知特征

得分低的内容会被忽略；高置信度内容默认自动保存；中等置信度内容进入托盘待确认队列。待确认队列只保存在当前进程内，完全退出程序后不会保留。

可在设置中关闭“高置信度通知直接收录”，让所有候选都先经过确认。

## 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl+N` | 新建/手动粘贴通知 |
| `Ctrl+Shift+V` | 立即从剪贴板导入 |
| `Ctrl+E` | 导出全部 Markdown |
| `Ctrl+F` | 搜索备忘录 |

## 数据与隐私

- 主数据库：`%LOCALAPPDATA%\NoticeMemo\noticememo.db`
- JSON 回退文件：`%LOCALAPPDATA%\NoticeMemo\noticememo.json`
- 设置文件：`%LOCALAPPDATA%\NoticeMemo\settings.json`
- Markdown 导出：`%USERPROFILE%\Documents\NoticeMemo\Exports`
- 演示数据：系统临时目录下的独立 `NoticeMemoDemo` 文件夹

所有通知和设置默认只保存在本机，不上传服务器。剪贴板内容只做本地规则判断；未达到通知阈值的文本不会保存。复制敏感内容前可先在托盘菜单中暂停自动捕获。

## 命令行

```powershell
# 后台启动，适用于开机启动或脚本
.\NoticeMemo.exe --background

# 使用独立数据目录，适合便携运行和测试
.\NoticeMemo.exe --data-dir .\local-data

# 唤醒已运行的实例后退出，可用于脚本
.\NoticeMemo.exe

# 通知已运行的后台实例干净退出
.\NoticeMemo.exe --exit

# 使用临时数据展示界面
.\NoticeMemo.exe --demo
```

## 构建与测试

环境要求：Windows 10/11 x64、[.NET 10 SDK](https://dotnet.microsoft.com/download)。

仓库根目录的 `NuGet.Config` 为离线开发保留了空包源，联网构建时请显式使用公开配置：

```powershell
dotnet restore NoticeMemo.slnx --configfile .github\NuGet.Config --disable-parallel -m:1
dotnet build NoticeMemo.slnx -c Release --no-restore -m:1
dotnet run --project tests\NoticeMemo.Core.Tests\NoticeMemo.Core.Tests.csproj -c Release --no-build --no-restore
```

生成自包含单文件 exe：

```powershell
.\scripts\publish-win-x64.ps1
```

离线环境或自定义包源可传入配置：

```powershell
.\scripts\publish-win-x64.ps1 -NuGetConfig .\work\NuGet.local.config
```

产物位于 `outputs\NoticeMemo-win-x64\NoticeMemo.exe`。`.gitignore` 默认排除构建目录、`outputs`、`work` 和二进制文件；建议将 exe 附加到 GitHub Releases，而不是直接提交进仓库。

## 项目结构

```text
src/
  NoticeMemo.App/      WPF 界面、托盘、剪贴板监听、设置与后台协调
  NoticeMemo.Core/     通知解析、捕获过滤、模型、SQLite/JSON 存储、Markdown 导出
tests/
  NoticeMemo.Core.Tests/  无外部依赖的控制台测试运行器
scripts/
  publish-win-x64.ps1  Windows 单文件发布脚本
docs/
  qq-listening.md      QQ 自动监听路线与合规边界
```

## 更新记录

### v0.2.0

- 新增后台剪贴板捕获、通知置信度过滤和托盘待确认队列
- 新增托盘常驻、单实例、唤醒和干净退出
- 新增设置持久化、暂停捕获、开机后台启动和气泡提示开关
- 新增 `--background`、`--data-dir`、`--exit` 命令参数
- 修复切换到“已完成/今天截止”筛选后，新导入备忘录不立即显示的问题
- 核心测试扩展到 10 项

### v0.1.0

- 完成通知解析、手动/剪贴板导入、搜索筛选、完成状态与 Markdown 导出
- 使用 SQLite 本地存储，系统 SQLite 不可用时自动回退到 JSON
- 提供自包含单文件 Windows x64 发布

## 后续方向

- 可选持久化待确认队列，避免程序退出时丢失候选
- 到期提醒、系统通知快捷操作和自定义提醒时间
- 更稳定的中文日期、会场与报名规则
- 在群主或管理员明确授权后，评估官方 QQ 机器人适配层
- 安装包、代码签名和自动更新

## License

暂未选择开源许可证。若计划公开发布，请先补充 `LICENSE`；在此之前保留所有权利。
