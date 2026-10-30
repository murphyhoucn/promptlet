# GPL 3.0 与第三方依赖核对

项目原始源码采用 `GPL-3.0-only`，另有针对 Windows App SDK 的附加链接许可。没有“或更新版本”授权；GPL 完整正文为根目录 LICENSE，附加许可完整原文在 LICENSE-NOTICE.md。版权使用“Promptlet contributors”，不包含私人邮箱或机器用户名。

## 许可范围

本声明覆盖 Promptlet 自有源码。NuGet 包、运行时、SDK 和其许可/声明适用各自条款，不因项目 LICENSE 文件而变更。发行二进制还需对应完整源码、构建脚本及适用的第三方声明；不能把 DLL 全部标成 GPL。

| 直接依赖 | 锁定版本 | 已核对的包元数据 |
| --- | --- | --- |
| Microsoft.WindowsAppSDK | 2.6.4 | 包内 Microsoft Software License Terms，非单一 MIT 声明 |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2957 | Windows SDK 许可 |
| System.Security.Cryptography.ProtectedData | 10.0.14 | MIT |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT，仅测试 |
| xunit | 2.9.3 | Apache-2.0，仅测试 |
| xunit.runner.visualstudio | 4.0.1 | Apache-2.0，仅测试 |

## 自包含 Windows 二进制与附加许可

当前工程设置 WindowsAppSDKSelfContained=true、SelfContained=true，既有运行 ZIP 附带 Windows App SDK。该包内条款包含 Windows 用途及再分发要求，不能只看到 SDK GitHub 仓库中的开源文件，就断言全部分发组件都兼容无例外 GPL。

项目使用 GPL 第 7 条的附加许可处理 Windows App SDK 组合/分发，不依赖未经确认的“系统库”认定。附加许可仅覆盖许可人有权授予的 Promptlet 部分，允许与 Windows App SDK（含 WinUI 3 和随附的未修改可再分发组件）组合及分发；第三方许可与声明仍须遵守。

该许可已获项目权利人明确批准，原文随源码和安装包提供；未给未知专有库授予通用豁免，也未将微软组件改标为 GPL。修改版本可按 GPL 第 7 条移除该附加许可。GPL 对 Promptlet 的对应源码等义务保留。

公开安装包从对应公开提交重新构建，并附带 GPL 正文、项目许可声明、第三方声明和源码获取方式。旧本地 ZIP 不作为该公开发行物。构建不输出本地 PDB，源码路径映射到固定公共路径以减少机器信息。

## 依据

- [GNU：如何为自己的软件使用 GPL](https://www.gnu.org/licenses/gpl-howto.en.html)
- [GNU GPL v3 正文](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU：GPL 与 Windows/不兼容库的常见问题](https://www.gnu.org/licenses/gpl-faq.en.html)
- [Windows App SDK NuGet 2.6.4](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.6.4)，以实际包内 license.txt 为准。

这些记录用于列出工程中的实际许可条件，不替代最终法律判断；也不对未知第三方代码或素材权属作保证。
