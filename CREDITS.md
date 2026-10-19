# 素材与参考来源

以下内容是按制作记录整理的来源说明，不是完整的权利清理报告或第三方授权证明。官方标识再分发许可及生图服务相关条款等尚未完成核验；范围见 [NOTICE.md](NOTICE.md)，权利反馈见 [RIGHTS.md](RIGHTS.md)。

| 项目 | 来源与使用方式 | 授权说明 |
| --- | --- | --- |
| 独立播放器、安装器、入口备份恢复 | 本项目自有 C# / PowerShell 实现 | MIT，见 LICENSE |
| 32 秒启动序列 | 本项目制作的绿色 Python 代码、状态窗口、工业 CODEX 和粒子动画 | 自有部分按 MIT 提供，见 NOTICE.md |
| 白发、紫眸、龙角人物 | 使用 gpt-image-2 生成，输入为纯暗色画布；不是用户照片，也未复用所参考包的人物图 | AI 生成内容，授权范围见 NOTICE.md |
| Codex 应用图标 | 从已安装的 Codex 桌面应用包提取，用于识别启动目标；出现在 EXE / assets/codex.ico 及动画中 | 属于相应权利人，不适用本项目 MIT |
| 中文字形 | Noto Sans SC / Source Han Sans 字体项目，用于预渲染动画中的中文 | SIL Open Font License 1.1；完整文本随包提供 |

## 视觉与交互参考

- 用户提供的 `codeflow.html`：代码流视觉概念的参考。本项目另行制作了绿色 Python 代码画面。本发布包未分发该原始 HTML；参考文件没有提供可核实的作者与开源许可，因此不将其标为本项目原创或 MIT 源码。
- [dsh-550c-boot](https://github.com/yannicksong0106/dsh-550c-boot)：启动终端、节点覆盖与状态切换的视觉参考；原项目作者 Ziyang Song，550C 引导片段在原项目中致谢 Voidpocket。本包使用本项目自己的画面与播放器，未打包或执行该插件。
- `dragon-codex-boot 0.2.0` 与 `codex-startup-animation 1.0.0`：用户提供的两个参考启动器，用于学习本地视频播放、独立 EXE 打包和入口接入方式。当前发布包没有再分发它们的源码、EXE、图片或视频。其发布者的 GitHub 地址未随本项目资料核实，故不猜测链接或补写授权。

## 工具与运行环境

本机制作使用浏览器画布渲染和 FFmpeg 导出。播放器与安装器通过 Windows .NET Framework 编译，日常播放使用系统 WPF 和媒体功能。本包不捆绑 FFmpeg、浏览器或 .NET 安装程序。

Noto 字体项目：[Noto CJK](https://github.com/notofonts/noto-cjk)。OpenAI 品牌资料：[OpenAI Brand](https://openai.com/brand/)。
