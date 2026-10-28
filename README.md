<p align="center">
  <img src="docs/assets/menfan-logo.png" alt="Menfan Screen Recorder 图标" width="144" height="144" />
</p>

<h1 align="center">Menfan Screen Recorder</h1>

<p align="center"><strong>Windows 原生录屏与轻量剪辑工具</strong></p>

<p align="center"><a href="https://github.com/ztmf123/menfan-screen-recorder/releases/tag/v0.5.0-beta.2">下载 Beta 测试版</a> · <a href="START_HERE.md">首次使用</a> · <a href="https://github.com/ztmf123/menfan-screen-recorder/issues">反馈问题</a></p>

Windows 原生录屏与轻量剪辑工具，灵感来自 [DogSC](https://github.com/laogou717/dogsc)（神烦老狗）。这是独立的 Windows 实现。

**v0.5.0-beta.2 · 公开测试版**：已按演示视频补齐主要录制、运镜与剪辑流程，支持全屏剪辑、黑白主题，并优化完整分辨率播放，默认使用完整分辨率预览；仍有功能差异和设备验证工作。目标系统为 Windows 10 22H2 / Windows 11 x64，本轮实机验证系统为 Windows 10 22H2。

## 使用与功能

启动只显示小型工具栏。选择显示器、窗口、区域或 DirectShow 设备，设置声音、麦克风、摄像头与保存位置。录制中可暂停/继续、添加标记、打开备忘录和切换桌面图标；停止后出现完成卡片，可保存项目或进入大编辑器。

录制前可勾选工具栏右下角“不录制软件面板”，选择会保存。面板始终可见、可操作，但会被 Windows 排除在录制画面之外；可以直接点击暂停或停止，也支持 Ctrl+Alt+R 和托盘停止。托盘使用直接停止动作，不弹出软件菜单。

- 多轨时间线、缩略图/波形、分割、拖拽裁剪与重排、变速、Q/W 波纹剪辑、撤销/重做。
- 纯色、渐变、网格、点阵、动态网格、图片、视频和 Windows 壁纸背景；画布比例、拖拽裁切、留白、圆角、描边、阴影和窗口/浏览器外框。
- 开场动画、点击自动缩放、手动缩放、3D 旋转与透视、聚焦虚化、动效曲线与相邻运镜过渡。
- 独立光标素材、触控点、隐藏、大小、平滑、时间偏移和点击反馈。
- 摄像头或导入视频画中画、形状蒙版、镜像和位置/大小动画；图片贴图、剪贴板图片和区域模糊。
- 系统声音/麦克风独立音量、混音预览、静音；手动保存/应用场景预设。
- 共享画面合成器用于预览/导出；MP4（H.264/AAC）或 M4A；720p、1080p、1440p、4K、原画布，30/60 FPS；取消导出保护。
- 自动保存录制项目，`.menfan` 工程继续编辑。工程以相对路径引用素材，迁移时请保留整个录制文件夹。

工具栏与编辑器均有“设置”入口，可切换黑底白字/白底黑字，立即生效并记住选择，两种模式均保留大字号，使用常规字重。F11 切换全屏剪辑，F 或“全屏预览”进入纯画面预览，支持播放、暂停和进度拖动；Esc 返回，保留播放位置和先前窗口状态。预览控制条会在闲置后隐藏，移动鼠标重新显示。

新录制和导入视频保持原始构图、空特效轨道，不自动生成缩放、点击反馈、开场、边框或场景预设。自动缩放只在“运镜 → 根据点击生成自动缩放”中由用户主动生成。已有工程可用“场景预设 → 清空画面效果”恢复原始画面，并保留剪辑片段、音轨和原素材。独立摄像头素材保留，需由用户选择显示。

预览默认使用完整分辨率，不再限制源画面为 2160 高度。系统支持的视频使用 Windows 原生播放器和实时画面合成，缩放、3D、模糊、光标等效果保留；不支持的编码格式及精细定位使用后台 FFmpeg。可手动切换“清晰预览”降低负载。实际流畅度取决于视频编码、系统媒体组件、显卡和场景复杂度。

关闭编辑器返回小工具栏并保留编辑状态，关闭工具栏退出。首次运行与快捷键见 [START_HERE.md](START_HERE.md)。

## 开发与验证

使用 .NET 8、WPF、Win32、NAudio 和外置 FFmpeg，无需 Electron。支持 Windows PowerShell 5.1 或 PowerShell 7。

```powershell
.\scripts\setup.ps1
.\scripts\run.ps1
.\scripts\test.ps1                  # 编译、媒体与场景合成
.\scripts\test.ps1 -Desktop         # 另加 WPF 窗口与裁切检查
.\scripts\test.ps1 -Desktop -Capture # 另加真实桌面/音频/暂停采集
.\scripts\publish.ps1               # 自带 .NET 的 Windows x64 ZIP
```

真实采集检查会录制主屏左上角 640×360 区域和系统声音，产物保存在本地 `artifacts/`。测试范围与结果见 [VALIDATION.md](docs/VALIDATION.md)。

`setup.ps1 -SkipSdk` 可使用已有 SDK，依赖保存到被忽略的 `.tools/`。FFmpeg 与 ffprobe 应在同一目录，当前验证组合为 FFmpeg 9.0.2 与 ffprobe 2023-02-13。自备 FFmpeg 需支持 gdigrab、DirectShow、libx264、AAC、标准滤镜及 `-/filter_complex` 文件参数；可设置 `MENFAN_FFMPEG` 或在应用提示中选择 `ffmpeg.exe`。

Visual Studio 2022 安装“.NET 桌面开发”工作负载，打开 `Menfan.ScreenRecorder.sln`，以 `Menfan.App` 为启动项目。

## 限制与发布

目前使用 FFmpeg GDI 采集，原生视频播放与 WPF 实时合成，并保留后台 FFmpeg 解码路径。最小化窗口、受保护内容、多屏 DPI、长录制同步与高负载性能仍需验证。4K/60 FPS 已完成短片编码检查，不代表实时采集或导出性能保证。Windows 11 和物理摄像头/采集卡尚未完成本轮实测。DirectShow 设备入口不等于 iPhone/iPad 原生有线采集。

完整差异见 [FEATURE_PARITY.md](docs/FEATURE_PARITY.md)，演示录屏的 49 项需求分析见 [VIDEO_STUDY.md](docs/VIDEO_STUDY.md)。

测试版说明见 [RELEASE_NOTES.md](RELEASE_NOTES.md)，发布流程见 [PUBLISHING.md](docs/PUBLISHING.md)。源码与默认便携包不含 FFmpeg 二进制；当前开发目录可使用 `.tools/ffmpeg/bin/ffmpeg.exe`。

## 结构与许可

- `src/Menfan.Core`：项目、时间映射、场景求值、媒体探测和音频混合。
- `src/Menfan.App`：录制、WPF 工具栏与编辑器、共享场景渲染器和导出。
- `tests`：媒体、场景、窗口和真实采集验证。

MIT。来源与依赖声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

当前图标采用用户选定的深色圆角设计，包含录像、红色录制点、胶片和剪刀。应用与 README 顶部使用同一张图片，资源说明见 [ICON_DESIGN.md](docs/ICON_DESIGN.md)。

编辑器左侧进入“运镜”，开启“跟随鼠标运镜（无需点击）”。默认放大 1.6 倍，画面随录制时的鼠标轨迹平滑移动，可调整跟随倍率和平滑时长。此功能默认关闭，不会改变新录制的白板状态；普通导入视频没有鼠标轨迹时无法使用，已有手动镜头片段优先。
