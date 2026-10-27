# Menfan Screen Recorder v0.5.0-beta.1

首个公开 Beta 测试版。面向 Windows 10 22H2 / Windows 11 x64 的原生录屏与轻量剪辑工具，灵感来自 DogSC。

## 下载与运行

解压 `Menfan-Screen-Recorder-v0.5.0-beta.1-win-x64.zip`，运行 `Menfan.ScreenRecorder.exe`。便携包自带 .NET 运行时，FFmpeg 与 ffprobe 需单独安装；首次使用见 [START_HERE.md](START_HERE.md)。校验文件为同名 `.zip.sha256`。

## 本版功能

- 小型录制工具栏，支持显示器、窗口、区域和 DirectShow 设备；系统声音与麦克风独立采集。
- 倒计时、暂停/继续、标记、备忘录，以及录制时隐藏软件面板。
- 新素材保持原始画面，剪辑和特效由用户自行添加。
- 多轨时间线、裁切、分割、重排、变速、撤销/重做；背景、光标、摄像头、缩放、3D 运镜与模糊。
- 默认完整分辨率预览、全屏剪辑和全屏预览；支持 MP4 视频及 M4A 音频导出。
- 黑底白字与白底黑字主题；保留放大的字号，改用常规字重。
- 滑条支持点击定位和拖动微调；暂停时调整 3D 倾斜使用实时合成预览。
- 应用、桌面快捷方式和 README 使用统一项目图标。

## 测试范围与已知限制

当前实机验证为 Windows 10 22H2；Windows 11、物理摄像头/采集卡、多屏 DPI、长时间音画同步和高负载录制仍需更多测试。1440p 与 4K 的短片播放、导出及 3D 参数交互已完成检查，这不代表所有硬件都能实时录制 4K/60 FPS。

FFmpeg 为外置依赖，不包含在此便携包中；DirectShow 入口不等于 Apple 设备原生有线采集。当前便携程序未签名。

本项目为独立 Windows 实现，与 DogSC 作者无隶属关系。功能差异见 [docs/FEATURE_PARITY.md](docs/FEATURE_PARITY.md)，验证记录见 [docs/VALIDATION.md](docs/VALIDATION.md)。

## 反馈

请在仓库 Issues 中提供 Windows 版本、显卡、操作步骤及预期/实际表现。涉及录制或预览问题时，补充视频分辨率、帧率和编码信息；可使用不含个人内容的短测试素材。
