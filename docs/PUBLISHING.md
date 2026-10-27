# GitHub 测试版发布

目标账号：`ztmf123`。仓库名：`menfan-screen-recorder`。首个公开测试版标签：`v0.5.0-beta.1`，发布名称：`Menfan Screen Recorder v0.5.0-beta.1`，应勾选 Pre-release。

仓库描述：Native Windows screen recorder and lightweight editor, inspired by DogSC.

## 发布内容

源码包括 `src/`、`tests/`、`scripts/`、`.github/`、`docs/`、`licenses/` 及根目录项目文件和文档。README 顶部保留 `docs/assets/menfan-logo.png`，图标位于项目名称上方；保留 DogSC 来源及第三方许可说明。

不上传 `.tools/`、`artifacts/`、`reference/`、`bin/`、`obj/`、用户录屏、个人工程与设备设置。使用 `.gitignore` 排除本地产物。首次提交前检查待上传文件清单。

## 构建和发布

1. 运行 `scripts/test.ps1 -Desktop`；真实录屏检查需交互桌面，按需添加 `-Capture`。
2. 运行 `scripts/publish.ps1`，得到自带 .NET 运行时的 Windows x64 ZIP。
3. 创建公开仓库，上传源码并等待 Windows CI。源代码的版本号应与发布标签一致。
4. 新建 `v0.5.0-beta.1` Release，勾选 Pre-release，将根目录 `RELEASE_NOTES.md` 作为说明。
5. 上传 `Menfan-Screen-Recorder-v0.5.0-beta.1-win-x64.zip` 及同名 `.zip.sha256`。源代码可由 GitHub 根据标签生成。
6. 检查 README 图标、源代码和 Release 附件可访问，再对外提供下载链接。

仓库的 Windows build 工作流会在 `main` 收到以 `Release v` 开头的提交时，先构建、测试、打包，再用当前提交创建对应 Beta 标签和 Pre-release，上传 ZIP 与校验文件。普通提交仅构建，不发布。也可手动运行工作流并开启 `publish_beta`。发布任务仅接受 `-beta.N` 版本；已存在的同名发布保留原样，不覆盖附件。

便携包不包含 FFmpeg。用户需单独安装 FFmpeg 与 ffprobe，并在程序提示中选择路径。发布范围与尚未验证的设备场景见 `RELEASE_NOTES.md`。
