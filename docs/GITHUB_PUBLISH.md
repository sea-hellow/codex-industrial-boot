# 上传到 GitHub

建议仓库名：`codex-industrial-boot`。

建议项目描述：`Unofficial Codex desktop startup animation for Windows, with a 32-second industrial cinematic and reversible shortcut setup.`

本文件只说明上传操作，不是授权审查结论。本版保持视频不变，第三方标识再分发许可及生图服务相关条款尚未完成核验；免费和非官方声明不能替代许可，也不能保证免于投诉或诉讼。范围见 [NOTICE.md](../NOTICE.md)，反馈处理见 [RIGHTS.md](../RIGHTS.md)。

## 1. 上传仓库源码

解压 **codex-industrial-github-source-v1.0.1.zip**，将里面 `codex-industrial-boot` 目录的内容上传到 GitHub 仓库根目录。README.md、LICENSE、NOTICE.md、RIGHTS.md、src、scripts、assets、licenses、docs 等应直接出现在仓库首页，不要只上传这个 ZIP，也不要把整个工作目录上传进去。

仓库源码包已包含 MIT 许可证、非官方声明、素材说明和发布说明。创建空仓库时无需再选择另一份 LICENSE，以免覆盖现有文件。

源码包还包含隐藏目录 `.github/ISSUE_TEMPLATE` 下的权利反馈模板。通过网页上传时也要保留该目录；在仓库 Settings → General → Features 启用 Issues，核对 New issue 中能看到「权利反馈 / Rights concern」。本包没有填入不存在的联系方式，也没有代你承诺私密邮箱已经可用。

## 2. 创建下载版本

在仓库中选择 **Releases → Draft a new release**：

1. 新建标签 `v1.0.1`。
2. 标题填写 `Codex Industrial v1.0.1`。
3. 将根目录 `RELEASE_NOTES.md` 的内容复制到发布说明。
4. 上传 **codex-industrial-startup-v1.0.1-win-x64.zip** 和对应的 **.sha256** 文件，作为下载附件。
5. 检查内容后点击 **Publish release**。

完整安装 ZIP 约 73 MB；放在 Releases 供用户下载。仓库源码包与 GitHub 自动生成的 Source code ZIP 都不包含视频或已编译 EXE。

项目文档和说明中已经声明非官方性质。请沿用该表述，不把项目介绍成 OpenAI 官方扩展。

发布页面应保留 RELEASE_NOTES.md 开头的非官方、虚构演出和授权边界说明；预览视频或截图的说明文字也应标明「非官方预制动画，画面中的认证与欢迎语不是官方表态」。这不会改变视频文件本身，也不能替第三方授予许可。

README、NOTICE.md 和 RIGHTS.md 应可从仓库首页访问。不要把「免费」「AI 生成」或已添加声明宣传为法律审核通过。收到具体权利问题时，按 RIGHTS.md 核验并处理，不以要求对方放弃权利作为处理条件。若授权无法确定，可选择暂缓公开相应附件。

## 3. 用户安装方式

用户先安装 Codex 桌面版，再下载 Releases 中的 Windows ZIP，完整解压并双击「安装启动动画.exe」。之后从已接入的桌面入口启动。README 包含预览、跳过、恢复原入口和任务栏固定项说明。

## 4. 从源码构建

在 Windows x64 中运行 `build.ps1` 生成两个英文命名的 EXE 及中文安装入口。构建工具使用系统 .NET Framework，无需下载依赖。

源码包保留 `media` 目录的说明文件，不含成片。要制作完整安装包，请从本项目 Windows Release ZIP 取出 `media/startup.mp4`，放入源码的相同位置后再构建。不能用 GitHub 自动生成的 Source code ZIP 直接安装或取代成片。

本地安装产生的 logs、.integration、原图标备份与配置记录不属于仓库内容；.gitignore 已排除这些文件。
