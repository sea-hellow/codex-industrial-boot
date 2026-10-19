# Codex Industrial · Windows 独立启动动画

**非官方社区项目，与 OpenAI 无隶属关系，未获其赞助、认证或背书。**

Unofficial community project. Not affiliated with, sponsored, certified, or endorsed by OpenAI. All third-party names and marks belong to their respective owners.

本项目为免费提供的社区视觉作品。视频中的错误、身份验证、`system access successful`、`welcome back` 与「chatgpt欢迎您」均为预制演出，不是实际安全检测、账号认证或 OpenAI 的官方表态。启动器不索取账号密码或 API 密钥。

**声明不等于第三方授权。** 官方图标的再分发许可及生图服务相关条款尚未完成核验；本项目不保证不存在侵权争议。请查看 [项目性质与授权范围](NOTICE.md) 和 [权利反馈与处理](RIGHTS.md)。

这是独立的 Windows 启动程序，不需要安装 Skill。32 秒高清工业风动画包括绿色 Python 代码与 CHATGPT WORK、多种故障窗口由红转绿、Codex 成功界面、工业 CODEX、粒子聚合、白发紫眸龙角人物和「chatgpt欢迎您」。

## 使用

1. 从本仓库 [**Releases**](https://github.com/sea-hellow/codex-industrial-boot/releases/latest) 下载 `codex-industrial-startup-v1.0.1-win-x64.zip`，完整解压，双击 **安装启动动画.exe**（或 Setup-Codex-Animation.exe），等待安装完成提示。
2. 以后点击原来的 **Codex / ChatGPT 桌面图标**，先播放动画，再进入 Codex。
3. **Preview-Animation.cmd** 只预览，不打开 Codex；**Start-Codex-Industrial.exe** 直接播放并打开 Codex。
4. **Restore-Codex.cmd** 恢复原来的入口。安装程序也保存在本机安装目录中。

GitHub 自动生成的 Source code ZIP 只供查看与构建源码，不包含完整视频与 EXE；直接安装请使用 Releases 中的 Windows ZIP。

安装文件复制到当前用户本地应用目录的 `CodexIndustrialNative` 文件夹。安装成功后，可以移动或删除解压目录和下载的 ZIP。无需管理员权限、Python、浏览器、FFmpeg、网络或 API 密钥。日常播放仅使用 Windows 自带的 .NET Framework / WPF 和媒体解码器。

安装器匹配当前用户桌面、开始菜单快捷方式和快速启动目录里的 Codex 入口，保留原名称与图标，并备份原始链接。支持原 Windows 应用快捷方式中 TargetPath 为空的情况。此前安装过本项目 Skill 版本时，会接管它的原桌面入口，并继承真正原始的图标备份。再次安装不会覆盖初次备份；恢复时只处理仍与安装记录一致的入口，保留用户后来修改或删除的图标。

Windows 注册的原生开始菜单应用条目、协议、直接执行官方程序的入口不经过本启动器。可使用新增的「Codex Industrial」开始菜单入口。任务栏可能缓存原入口；如旧固定图标未生效，请取消固定后，从已接入动画的桌面入口重新固定。本程序不修改 Codex 应用文件，也不常驻监控其他进程。

## 播放与画质

视频是 **2560×1600、30 帧/秒、32 秒 H.264**，保持原始编码，不重复压制。播放器覆盖鼠标所在显示器，保持比例，其他比例屏幕可能出现窄黑边。按 Esc / Enter / 空格或右下角「跳过」立即进入 Codex。关闭动画窗口也会继续打开 Codex。缺失视频、无法解码或播放停滞时，普通启动会继续打开 Codex；预览失败则只关闭预览。遵循 Windows 减少动画设置；需要完整查看时使用预览入口。

适用于已安装 Codex 桌面版的 Windows 10/11 x64；Windows N 等精简系统需要媒体功能。默认目标为已注册的官方桌面应用，不是终端 codex 命令。其他桌面安装可在安装前修改 `launcher.config.xml` 的 `executablePath` 与参数。配置时长不会改变已编码的视频；修改动画需要替换 MP4。

## 构建与诊断

附带独立播放器和安装器源代码。`build.ps1` 使用 Windows 自带的 .NET Framework x64 编译器构建两个 EXE 及中文安装入口，无需下载包。成片通过 Releases 提供，动画渲染工程未包含在当前源码包中；重新构建 EXE 不会重新生成 MP4。参考项目与素材来源见 [CREDITS.md](CREDITS.md)。

本机安装目录下 `logs/last-session.json` 记录播放是否完成、激活是否成功、返回的应用进程以及是否检测到窗口。`.integration/state.json` 保存入口与备份；`.integration/report.json` 保存最近安装或恢复结果。请保留已安装目录，或先恢复入口再删除它。

开发验证参数：`--preview`、`--force-animation`、`--reduced-motion`、`--check`、`--report <文件>`。`--skip-after <秒>` 只用于验证跳过与交接。安装脚本支持独立测试目录和搜索目录，不需要在真实桌面运行恢复测试。

已在一台 Windows x64 电脑验证整段播放、原桌面入口激活、安装恢复及失败处理。激活验证时 Codex 已打开，其他电脑的冷启动时间尚未验证，详细结果见 [validation.json](validation.json)。

## 许可与来源

项目自有代码采用 [MIT License](LICENSE)。完整范围、AI 生成内容与品牌标识说明见 [NOTICE.md](NOTICE.md)，素材致谢见 [CREDITS.md](CREDITS.md)，第三方说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。Codex / ChatGPT 名称及图标属于相应权利人，不在本项目 MIT 授权范围内。

本仓库的当前发布包免费提供，不包含付费解锁或广告。不收费不能替代许可核验；此说明也不对自有代码的 MIT 授权追加「仅限非商业」条件。任何人后续使用第三方标识或素材，仍需独立确认其适用规则。

权利人或代理人可通过本仓库 [Issues 的「权利反馈 / Rights concern」入口](https://github.com/sea-hellow/codex-industrial-boot/issues/new/choose)指出具体问题，处理方式见 [RIGHTS.md](RIGHTS.md)。无需先联系维护者才能依法主张权利。

发布此项目的步骤见 [GitHub 上传说明](docs/GITHUB_PUBLISH.md)，发布文案见 [RELEASE_NOTES.md](RELEASE_NOTES.md)。
