# Promptlet 开发与发布

对应软件 0.2.2，2026-10-30。

## EXE 安装包

普通用户的首选发行物为 `Promptlet-0.2.2-Setup-x64.exe`。应用二进制与原 v0.2.2 完全相同；仅增加 Inno Setup 安装向导，不改变应用版本或已有标签。

先通过 `scripts/package.ps1` 生成完整 ZIP 及其目录，再安装 Inno Setup 6.7.4 或以上，运行 `scripts/build-installer.ps1 -CompilerPath "实际安装目录\ISCC.exe"`。该脚本校验清单内所有 App 文件，使用 `packaging/Promptlet.iss`、仓库内中文语言资源及编译器随附许可生成单个 EXE 和 SHA-256 文件；可通过 `-PackagePath` 复用已验证的发行目录。

安装器仅支持 Windows 11 x64，当前用户权限安装；注册开始菜单和系统卸载入口，可选桌面快捷方式。安装前通过原有退出事件请求应用关闭；保留既有自启动偏好，卸载仅移除指向本安装路径的自启动项。配置位于应用安装目录外，安装和卸载均不删除。源代码仍为 GPL-3.0-only 加 Windows App SDK 附加许可；Inno 引擎声明随安装分发。中文语言文件来自 Inno Setup 官方仓库，保留维护者公开署名；这不是项目使用者的个人信息。

验证安装器时可使用 `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TESTMODE=1 /DIR=工程内隔离路径`。`TESTMODE=1` 仅关闭快捷方式、启动和旧版注册表迁移；仍写入 `Promptlet_is1` 卸载项，因此必须先确认该项不存在，不能覆盖已有 EXE 安装注册。重复安装后校验全部文件、运行隔离的原生烟测，再以 `unins000.exe /VERYSILENT /NORESTART /TESTMODE=1` 验证卸载。该模式不代替正常安装入口和真实配置保留的验收。

原始 v0.2.2 标签和源码 ZIP 保留不变。新增的 `Promptlet-0.2.2-installer-source.zip` 包含完整应用源码及安装器脚本，对应安装器补充提交；发行说明记录其提交 ID。未来版本应在打标签前完成安装器构建和验收。

## 环境与构建

Windows 11 x64；SDK 10.0.403（global.json）；WinAppSDK 2.6.4；.NET 10。PowerShell 脚本优先调用 `.tools/dotnet/dotnet.exe`。CLI 构建不依赖打开 Visual Studio；使用 IDE 时应选择支持 .NET 10 的版本。

执行 `scripts/setup.ps1`、`scripts/build.ps1`。构建与测试均使用锁定 NuGet 还原。改变依赖后显式还原对应项目并审查 `packages.lock.json`；不要为绕过错误关闭锁定或启用实验依赖。

## 模块约束

- Core 不引用 WinUI 或 Windows 类型。配置记录不可变；编辑页复制数组和记录，不能修改已保存的集合。
- `WorkspaceSettings.ResolveRequest` 解析当前模型和模板为独立请求快照；进行中的任务不读取之后保存的配置。
- `ConfigurationCodec` 的 v2 envelope 包含 Settings 和按 ProfileId 索引的 Secrets；保护器由 Windows 层注入。Key、无法解密的密文状态都是 JSON 忽略字段。
- 模板的内置身份按 ID / BuiltInKind 判断，与用户名称无关。缺失当前模型不回退；删除当前模板的 UI 路径回退内置科研项。
- `SettingsService` 只操作当前数据目录。保存前完成编码；旧格式第一次保存建立备份，使用临时文件替换原文件。格式不可读时禁止保存。
- `OptimizationCoordinator` 保持提交、取消、替换的一致顺序；不要为了改善 UI 流畅度直接把提交校验移出协调器。
- `StallProtectedStream` 对每次底层读操作设置停滞超时；请求总超时单独覆盖整个请求。SSE 按完整事件解析，不能假设一个读块就是一条消息。
- `AutoHidePolicy` 只在任务状态变化、窗口打开或用户交互时重置；不在每帧重绘时重置。忙碌阶段与编辑阶段暂停。
- 原生窗口须清除残留 WS_DLGFRAME / WS_BORDER / WS_THICKFRAME 和扩展边框样式，使用 FRAMECHANGED 刷新。根背景铺满客户区，由 DWM 处理外部圆角。
- 提示词卡片内的 TextBox 要同时覆盖常态、悬停、焦点与禁用的背景 / 边框资源；仅设置 Background=Transparent 会被 WinUI 模板状态覆盖。资源仅作用于浮窗，设置页输入框保留原生样式。
- 设置列表采用 40 DIP 单行容器与上下合计 8 DIP 内边距，实际高度按项目数计算，最多四行；不要恢复为固定空白高度。空模型列表收起并显示说明，长名称省略且可查看提示。
- 浮窗拖动从 Root 的 PointerPressed / Moved / Released 处理，不在 Logo 按下时发送 WM_NCLBUTTONDOWN。捕获发起拖动的鼠标指针，按当前 DPI 的系统阈值判断开始，以屏幕像素差调用 AppWindow.Move；每次移动复查左键真实状态。释放、取消、捕获丢失、失焦、隐藏及 Esc 都清除状态。
- Root 使用透明背景参与空白处命中；按钮及其图标、正文文本框、滚动条不开始窗口拖动。空态只读文本框不参与命中，便于从卡片空白拖动；编辑或出现正文后恢复命中。

## 修改与调试流程

1. 新建本地分支或保存工作基线，先复现问题。
2. Core 行为优先增加失败或边界测试；运行 `scripts/test.ps1`。
3. 执行 `scripts/build.ps1` 后运行窗口；脚本正常退出工作目录构建 / 发布版本，清理热键和托盘。
4. `scripts/smoke.ps1` 实际创建 WinUI 控件、编辑模板和模型、保存合成密钥，并检查客户区与窗口几何。运行时应没有其他正在使用的 Promptlet 实例。
5. 使用本机模拟服务验证协议。需要真实接口时，在界面配置，不把 Key 写入代码或命令行。
6. 视觉检查涵盖长标签、深浅主题、激活与失焦、不同 DPI、关闭透明效果。构建通过不等于视觉通过。

日志位于当前数据目录 `errors.log`，不含请求 / 正文 / Key。查看日志时先核对当前运行路径及设置页路径，避免把旧发布版或其他宿主重定向副本误认成当前版本。

0.2.2 烟测增加一行模型、两行模板、六项模型内部滚动，以及深浅主题下原文 / 结果文本框常态、悬停、焦点状态的实际模板背景检查。`UiSmokePreview` 只在隔离烟测内渲染应用自己的 XAML 树，输出到当次报告目录，不抓取桌面或读取真实用户配置。PNG 用纯色替代树外的系统材质，供布局与文字对齐复核；不要据此宣称已验证 DWM 材质或所有 DPI。

## 拖动问题定位与回归

旧版 `PromptWindow.xaml` 只给 Logo 所在 StackPanel 绑定 `PointerPressed="DragHeader"`，页面其他部分没有拖动入口。旧处理器在按下时立即 `ReleaseCapture()` 并发送 `WM_NCLBUTTONDOWN / HTCAPTION`，且屏幕坐标 lParam 固定为 0，没有拖动阈值或配套的释放状态管理。结合用户现象，推断是这条绕过 XAML 指针生命周期的系统移动路径留下了异常移动状态；不将该推断当作已完成的 OS 消息追踪结论。

修复位于 `PromptWindow.xaml`（根命中层）、`PromptWindow.xaml.cs`（指针捕获、控件排除与释放清理）、`Services/WindowDragGesture.cs`（阈值、屏幕坐标及状态），以及 `NativeMethods.cs` 的 DPI 系统阈值查询。Core 和其他窗口不变。释放与捕获丢失按 [Microsoft 的指针输入文档](https://learn.microsoft.com/en-us/windows/apps/develop/input/handle-pointer-input) 处理。

Windows 测试链接实际生产用的拖动状态类，新增 8 个用例覆盖点击 / 抖动、阈值、松开后移动、漏收释放事件、取消后重拖、不同指针及负坐标。原生烟测检查真实控件命中排除、空态卡片透传 / 编辑和正文恢复命中，并用合成手势状态实际移动 AppWindow，检查原生位置与无边框状态，隐藏后验证清理。合成状态测试没有注入操作系统鼠标输入，不冒充物理鼠标端到端测试。

Computer Use 无法发现当前托盘型 prompt 浮窗，返回 `launched app did not expose a targetable window`，本轮不宣称完成该工具的鼠标验收。手工步骤见验证文档。拖动逻辑应保持非模态，不重新引入 Logo 点击时的系统移动消息。

## 图标管线

`Assets/logo.json` 是几何和颜色来源。运行 `python scripts/generate_icons.py` 生成明暗 PNG、SVG 与 7 档 ICO。查看 LogoLight.png、LogoDark.png，并检查 16/24/32 像素下的辨识度。不要独立编辑生成的 SVG 与 Python 中第二套几何。

## 测试与发布

核心测试包括状态协调、回环 HTTP、UTF-8、SSE、超时、格式迁移和计时；Windows 测试链接实际保存服务，在独立临时目录中进行真实 DPAPI 往返。禁止读取或覆盖真实用户配置作为测试夹具。

运行 `scripts/publish.ps1` 生成完整自包含文件夹。WinUI 发布需要额外复制 XBF / PRI；脚本检查这些资源与图标存在。升级 SDK 后必须重新检查发布目录启动。

运行 `scripts/package.ps1` 生成 ZIP、逐文件哈希和 ZIP SHA-256。安装器只写当前用户 Programs、开始菜单和 HKCU 卸载项；卸载只删除带正确安装标记的产品版本目录，不删除用户配置。升级后更新已存在的自启路径。

发布前核对安装后的 EXE 版本、启动、单实例、退出和卸载注册信息。发布 ZIP 不能包含 `.tools`、用户设置、真实 Key、`.git` 或开发烟测的合成配置。源码可用 `git archive` 单独打包，不包含 SDK 与构建产物。
