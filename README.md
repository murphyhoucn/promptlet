# Promptlet 0.2.2

Windows 11 x64 提示词优化工具：**复制 → Ctrl + Alt + Space → 自动优化并复制 → 粘贴。**

采用 WinUI 3、C# / .NET 10，不使用 WebView 渲染界面。常驻托盘，直接连接用户配置的 Chat Completions 兼容接口，无账号或中转服务器。

0.2.2 修复提示词浮窗拖动：单击 Logo 后松开再移动鼠标不会带动窗口；按住页面空白、静态标题、Logo 或空态只读卡片可拖动。移动超过系统阈值才开始拖动，松开或窗口隐藏后立即结束。有正文或处于编辑模式时保留文字选择和输入；按钮点击保持原有功能。既有业务逻辑和 v2 配置格式兼容，继续包含 0.2.1 的卡片底色和紧凑列表修复。

## 获取、安装和启动

直接下载 **[Promptlet-0.2.2-Setup-x64.exe](https://github.com/murphyhoucn/promptlet/releases/download/v0.2.2/Promptlet-0.2.2-Setup-x64.exe)**，双击后按中文安装向导操作即可。无需解压、命令行、管理员权限或开发环境。完成后打开设置配置自己的模型服务。可从开始菜单启动，在 Windows“设置 → 应用 → 安装的应用”中卸载；卸载保留用户配置。

项目仓库为 [murphyhoucn/promptlet](https://github.com/murphyhoucn/promptlet)，完整资产见 [Releases](https://github.com/murphyhoucn/promptlet/releases)。每个安装包附有 `.sha256` 校验文件。`Source code (zip/tar.gz)` 和 `source.zip` 是开发者源码，普通用户选择上述 **Setup-x64.exe**。EXE 使用原 v0.2.2 应用文件，新增安装向导，业务功能保持一致；安装器源码另附 `installer-source.zip`。

也保留原 ZIP 供便携运行或脚本安装。完整解压 Windows x64 ZIP 后选择：

- **安装**：双击 `Install.cmd`，按当前 Windows 用户安装，无须管理员权限；创建开始菜单入口和系统“已安装的应用”卸载项。
- **直接运行**：启动 `App/Promptlet.exe`。必须保留整个 `App` 文件夹，不能只拷贝 EXE。

安装位置为 `%LOCALAPPDATA%\Programs\Promptlet\0.2.2\`。安装脚本在复制前校验应用文件 SHA-256；升级已有启用的开机启动项时更新程序路径。卸载保留用户配置和加密密钥。

发布包自包含 .NET 和 Windows App SDK，不要求用户安装 .NET SDK，依赖许可和声明随 App/Licenses 分发。未提供代码签名、MSIX、商店发布或自动更新；当前已在开发电脑 Windows 11 上部署验证，未做全新系统镜像认证。原生依赖基于 Windows 11 系统组件。

## 初次配置

1. 打开设置，点击模型配置的“新建”。
2. 填写别名、Base URL、API Key、模型 ID 和流式开关。例如 Base URL 为 `https://api.example.com/v1`。
3. 点击“测试此模型”，成功后点击“用作当前模型”。测试发送固定短文本，可能产生少量模型费用。
4. 选择内置或自定义模板，点击“用作当前模板”，最后“保存设置”。
5. 复制文字并按快捷键；完整成功后直接粘贴。

选择列表项是在编辑该项，不代表切换实际使用项。设置更改在保存后统一生效；关闭窗口丢弃未保存修改。未填写完整的连接可以作为草稿保存，但调用前必须完善。

## 主要功能与行为

- 多模型连接完全独立，分别保存地址、Key、模型 ID、流式开关；浮窗显示本次任务实际使用的模型别名和模板名。
- 模板可新增、改名和编辑，不设置业务数量上限。两个内置模板不能删除；恢复内置正文保留用户改名。
- 删除当前自定义模板回退内置科研模板；删除当前模型进入未配置态，不自动改用其他供应商。
- 生成期间暂停自动隐藏；成功、复制被阻止、复制失败、请求失败或点击取消后，重新给予完整倒计时。默认 5 秒，可设为 0 关闭倒计时。
- 点击窗口外、切换应用或点击隐藏仍立即隐藏，请求继续。Esc 取消未完成请求并立即隐藏。
- 编辑原文时暂停计时；点击重新优化或 Ctrl + Enter 才提交。隐藏后重新打开保留编辑草稿，“放弃编辑”恢复通常的剪贴板工作流。
- 用户等待时复制了新内容，保留新剪贴板；提供手动“复制结果”。断流、截断、空结果、取消不会自动复制。
- 同时只执行一个任务，重复唤起不重复请求；读到自己上次复制的结果时只展示。
- 浅 / 深 / 跟随系统主题、系统材质、无外框浮窗、两张内容卡片。主按钮跟随系统强调色，Logo 与胶囊使用品牌绿。

完整说明见 [使用文档](docs/user-guide.md)。

## 配置、迁移与日志

设置页会显示**当前应用实际使用的配置路径**，并提供打开目录按钮。默认位置为 `%LOCALAPPDATA%\Promptlet\settings.json`；从某些宿主启动可能发生路径重定向，以界面显示为准。

v2 格式使用稳定 ID 保存模板和模型，密钥逐项 DPAPI 加密，普通配置不序列化明文 Key。旧版配置读取时自动迁移到内存，首次保存新格式前保留 `settings.json.v1.backup`。保留旧模板内容、模型、主题、快捷键与自启偏好。单条密钥解密失败时原密文保留，可明确清除或替换，不清空其他项。

无法读取或遇到未来格式时进入只读保护，禁止覆盖。备份与密文也应妥善保存；DPAPI 密文通常不能迁移到另一 Windows 用户。不要把配置文件公开上传。

不保存提示词历史，不轮询剪贴板。最近结果和草稿仅在进程内存中。错误日志 `errors.log` 只记录异常类型、堆栈与时间，超过约 512 KB 保留一个轮转文件，不记录 Key、请求体或模型正文。

## 开发：环境与依赖

源码工程要求 Windows 11 x64、PowerShell、.NET SDK 10.0.403。`setup.ps1` 将官方 SDK 安装到项目 `.tools/dotnet/`，不修改系统 PATH。依赖由 NuGet 自动还原，首次构建需要网络，版本固定在项目和 lock 文件中。

```powershell
.\scripts\setup.ps1
.\scripts\build.ps1            # Release 构建 + 全部测试
.\scripts\run.ps1              # 首次配置，之后托盘常驻
.\scripts\run.ps1 -Settings
.\scripts\run.ps1 -Show        # 仅展示，不自动调用模型
.\scripts\run.ps1 -Quit
```

已提交的图标资产可直接构建；修改 Logo 后需 Python 3 执行 `python scripts/generate_icons.py`，无第三方 Python 依赖。

## 开发：目录与模块

```text
src/Promptlet.Core/          配置模型/迁移、优化请求、SSE、任务协调、隐藏计时策略
src/Promptlet/               WinUI 窗口、托盘/快捷键、原生剪贴板、DPAPI 与文件保存
src/Promptlet/Assets/        logo.json 单源参数和生成的 PNG/SVG/ICO
tests/Promptlet.Core.Tests/  配置、迁移、协议、超时、状态与并发测试
tests/Promptlet.Windows.Tests/ 实际 DPAPI、备份、只读保护测试（隔离目录）
packaging/                  Inno Setup 安装向导、中文语言资源及兼容安装/卸载脚本
scripts/                    构建、测试、发布、打包、原生烟测、资源采样
docs/                       使用、开发、验证与许可记录
```

`WorkspaceSettings` 是可保存的配置；`AppSettings` 是解析后用于一次请求的独立快照（同时保留旧格式迁移兼容）。`OptimizationCoordinator` 负责单任务、取消与剪贴板提交的串行裁决。UI 不负责网络鉴权和密钥持久化。`ConfigurationCodec` 管理格式版本与迁移，Windows `SettingsService` 负责 DPAPI 和原子文件替换。

## 开发：验证、发布和调试

```powershell
.\scripts\test.ps1             # 核心测试 + Windows 持久化测试
.\scripts\smoke.ps1            # 原生 XAML/CRUD/DPAPI/窗框烟测，隔离数据，不读真实剪贴板
.\scripts\publish.ps1          # 自包含目录 artifacts/Promptlet-win-x64
.\scripts\package.ps1          # 测试、发布、文件哈希、离线 ZIP
.\scripts\build-installer.ps1 -CompilerPath "Inno Setup 安装目录\ISCC.exe"
.\scripts\measure.ps1 -Seconds 60 -Published
python .\scripts\mock_api.py  # 本机模拟协议服务；不是大模型
```

模拟接口使用 `http://127.0.0.1:18765/v1`、模型 ID `mock`、Key 留空。可用 `--mode truncated`、`--mode auth-error`、`--delay 0.5` 检查异常与取消。不要把模拟结果当作优化质量验证。

生成 EXE 另需安装 [Inno Setup](https://jrsoftware.org/isdl.php) 6.7.4 或以上，把上面 `CompilerPath` 替换成实际编译器路径；先运行 `package.ps1`，安装器再核对 App 清单 SHA-256 并压缩为单个 `artifacts/release/Promptlet-0.2.2-Setup-x64.exe`。可用 `-PackagePath` 指定已验证的完整发行目录。仓库不包含 Inno 编译器或用户设置。中文资源来自 Inno Setup 官方仓库，第三方署名及许可保留；安装器在当前用户范围写入文件、快捷方式和卸载项，不默认开启自启动。

`PROMPTLET_DATA_DIR` 可为开发 / 烟测指定隔离配置目录；生产通常不设置。`PROMPTLET_FRAME_DIAGNOSTICS` 指定可选原生窗框 NDJSON 输出。`smoke.ps1` 自动设置隔离路径，输出 `artifacts/latest-smoke.json`，并在本次 `artifacts/smoke-*` 目录生成深浅主题的 XAML 控件 PNG 预览。预览使用纯色主题背景，不包含 DWM 毛玻璃和原生标题栏；窗框另外通过原生几何检查。它们不能替代全部 DPI 与桌面环境的视觉验收。

回滚：退出托盘中的新版，再运行保留的 `%LOCALAPPDATA%\Programs\Promptlet\0.2.1\Promptlet.exe`。需要把开始菜单、卸载与自启入口也恢复至旧版时，重新执行保留的 0.2.1 发布包中的 Install.cmd。回滚或升级均保留现有配置；不要同时运行两个版本。

更多工程与调试细节见 [开发文档](docs/development.md)，实际验证边界见 [验证记录](docs/verification.md)。

## 常见问题

| 问题 | 处理 |
| --- | --- |
| 关闭窗口后仍运行 | 正常；从托盘右键退出 |
| 快捷键无反应 | 从开始菜单或托盘打开设置，检查冲突；保存失败会保留旧绑定 |
| 有模型但提示未配置 | 选择该项后点“用作当前模型”，再保存 |
| 模板 / 模型改名没出现在浮窗旧结果上 | 旧结果保留生成时的名称；下次新请求使用新配置 |
| 找不到设置 | 以设置页显示路径为准；未保存过时文件尚不存在 |
| 没有自动复制 | 检查剪贴板更新、响应截断或失败状态；完整结果可以手动复制 |
| 开机启动路径失效 | 安装新版本会更新已有启动项；手工移动便携目录后重新关闭并开启自启 |
| 接口不支持流式 | 关闭该模型的流式开关后重新测试，不会自动重复计费重试 |
| Key 解密失败 | 在同一 Windows 用户下使用，或替换 / 明确清除此项 Key；其他项仍保留 |

## 验证范围

已在 Windows 11 x64 验证构建、67 项测试和隔离原生烟测。协议测试使用本机 HTTP 与合成数据，不消耗真实 API 额度。真实供应商兼容性与优化质量取决于用户配置。未完成全新系统镜像、全部 DPI / 高对比度组合或真实鼠标端到端自动验收，详见验证文档。

## 许可证

项目自有源码采用 **GNU GPL 3.0 only（GPL-3.0-only），附 Windows App SDK 链接许可**，见 [LICENSE](LICENSE) 与 [许可声明](LICENSE-NOTICE.md)。版权标记为“Promptlet contributors”。

附加许可依据 GPL 第 7 条，仅允许与 Windows App SDK 及随附的未修改可再分发组件组合和分发；GPL 对 Promptlet 源码的义务保留。第三方组件继续遵循各自许可，完整声明随安装包保留，详见 [许可核对](docs/licensing-review.md)。Release 安装包由对应公开源码重新构建，不复用含本地调试路径的旧 ZIP。

实际 settings.json、DPAPI 密文及备份、环境文件、SDK 缓存、日志和私人验收记录不进入公开源码。需要报告安全问题时，不要在公开 issue 中提交密钥、真实提示词或用户配置。发布前检查结果会在本地保留，公开范围采用逐文件白名单。
