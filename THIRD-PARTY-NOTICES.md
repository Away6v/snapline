# 来源与许可

SnapLine 是参考 [Tendedero](https://github.com/alejandrobujan/tendedero) 的 Windows 原生实现，使用 C#、WPF 和 Windows 桌面 API 重新实现界面与系统交互。

- 参考提交：`32a3e47187fdea7b2fd43b3ab5a913f0ed33724a`。
- 上游作者：Alejandro Buján；代码采用 MIT 许可，版权声明保留在本仓库 LICENSE。
- 上游名称、图标及 docs 中的图片不包含在其 MIT 授权中。本项目不复制这些资产，亦不代表上游作者认可本项目。
- SnapLine 图标为本项目原创的代码绘制资产，适用本仓库 MIT 许可。
- 便携包包含 Microsoft .NET 10 与 Windows Desktop Runtime。其许可及第三方声明随运行时文件一并分发；.NET 源码与许可见 https://github.com/dotnet/runtime 和 https://github.com/dotnet/wpf。

应用没有分析 SDK。手动检查更新时，应用会连接 GitHub 获取发布信息和更新包。GitHub Actions 与构建工具联网下载构建依赖，不属于应用运行行为。
