# Miliastra Archive Tool · 千星奇域存档上传工具

我是一名千星奇域爱好者。制作关卡后，如果想把存档上传到多个服务器，就需要反复切换服务器、导入存档、填写关卡信息和处理图片资源。这些重复操作并不方便，因此我决定制作这个工具，让多服务器存档上传更省事一些。

Miliastra Archive Tool 是一个 Windows 桌面工具。你可以提前整理存档、各语言的介绍和图片等资源，再由工具根据游戏画面逐步完成上传操作。希望它能帮助同样喜欢千星奇域的创作者，把更多时间留给关卡创作。

本项目由爱好者独立开发，与游戏官方没有隶属或合作关系。

## 下载

- [下载 Windows x64 完整程序](https://github.com/zybpl/MiliastraArchiveTool/releases/download/v2026.10.26/MiliastraArchiveTool-20261026-win-x64.zip)
- [查看发布说明与校验文件](https://github.com/zybpl/MiliastraArchiveTool/releases/tag/v2026.10.26)

完整解压后运行 `MiliastraArchiveTool.exe`。下载包包含 .NET 运行时、识别模型和中英文手册；使用程序不需要安装 .NET SDK。源码 ZIP 供开发者使用。

## 能做什么

- 管理本地存档及其介绍、标签、图片和视频配置。
- 按任务配置，在不同服务器执行存档上传或更新流程。
- 填写预览信息及多语言内容，处理图片资源草稿和提交审核。
- 对关键步骤进行结果检查；遇到异常时记录诊断信息，支持在限定页面内继续任务。

图片资源的**提交审核不等于发布**。通过审核后需要手动发布的内容，仍需在游戏中完成。

## 基本原理 / How it works

### 中文

工具采用“**截图 → 识别 → 操作 → 检查结果**”的方式工作，通过 Windows 的鼠标、键盘输入和文件选择窗口交互来操作游戏。

1. **获取画面**：捕获游戏窗口，作为当前页面状态的依据。
2. **定位界面元素**：使用 YOLO 模型识别页面和按钮、卡片、输入区域等目标，通过 ONNX Runtime 在本机推理。
3. **结合文字与图像判断**：使用 RapidOCR 读取目标区域内的按钮文字、存档标识、上传计数等；对固定的语言名称和小图标，使用 OpenCV 模板匹配辅助定位。识别任务在本机执行，无需把截图发送到云端 OCR 服务。
4. **执行操作**：依据识别结果点击、滚动、选择文件和填写内容。多语言文本通过 Unicode 剪贴板粘贴；同一表格视区内复用已确认的列坐标，并核对语言行，减少重复定位。
5. **确认结果后继续**：检查页面是否切换、表格是否稳定，以及上传计数或状态是否变化。部分步骤通过多帧确认和有限重试处理加载延迟；失败后保留日志和诊断截图，供排查或继续任务使用。

它依赖游戏界面的实际显示，窗口遮挡、界面改版、加载延迟和识别误差都可能影响运行。多语言正文的 OCR 回读用于辅助发现异常，不能保证逐字校验正确；上传后仍应核对游戏内的实际内容。

### English

The tool follows a **capture → recognize → act → verify** loop. It operates the game through Windows mouse and keyboard input and interactions with file selection dialogs.

1. **Capture the game window:** A screenshot provides the current visual state.
2. **Locate interface elements:** A YOLO model detects pages and targets such as buttons, archive cards, and input areas. Inference runs locally with ONNX Runtime.
3. **Combine text and image evidence:** RapidOCR reads text within relevant regions, including button labels, archive identifiers, and upload counters. OpenCV template matching helps locate fixed language labels and small icons. Recognition runs locally without sending screenshots to a cloud OCR service.
4. **Perform the action:** The tool clicks, scrolls, selects files, and fills in information based on the detected state. Multilingual text is pasted through the Unicode clipboard. Confirmed column positions are reused within the same table viewport while language rows are checked to reduce repeated localization.
5. **Verify before proceeding:** The workflow checks page transitions, table stability, and changes in upload counters or status. Selected steps use multiple observations and bounded retries to handle loading delays. On failure, logs and diagnostic screenshots support troubleshooting and task continuation.

The tool depends on the visible game interface. Obstructed windows, UI updates, loading delays, and recognition errors can affect execution. OCR readback of multilingual content helps detect anomalies but does not guarantee an exact character-by-character match. Please review the actual content in the game after uploading.

### 识别与操作流程 / Recognition and action flow

```mermaid
flowchart TD
    A[捕获游戏画面 / Capture game window] --> B[YOLO 定位 / Locate targets]
    B --> C[按需 OCR 与模板匹配 / OCR and template matching as needed]
    C --> D[判断当前状态 / Determine current state]
    D --> E[执行鼠标键盘操作 / Perform mouse and keyboard actions]
    E --> F[检查结果 / Verify result]
    F -->|成功 / Success| G[下一步骤 / Next step]
    G --> A
    F -->|等待或有限重试 / Wait or bounded retry| A
    F -->|仍未通过 / Still unsuccessful| H[记录诊断并中断 / Save diagnostics and interrupt]
```

## 使用前准备

- 使用 Windows x64，并让游戏以**窗口模式**运行。建议从 **1920 × 1080** 分辨率开始测试，确保窗口完整可见。
- 游戏界面目前适配**简体中文和英文**。存档介绍可配置 15 种语言；这与游戏界面语言是两个不同的设置。
- 检查存档默认语言与游戏语言是否一致，准备好 `.gil` 存档及需要的媒体文件。
- 建议从**开始游戏界面**启动任务，由预检确认起点和语言后再继续。
- 任务运行期间保持游戏窗口可见，避免手动操作鼠标、键盘或更改剪贴板。
- **等待沙箱启动需要耐心**：不同机器的启动速度不同。这一步没有统一的实际耗时，请结合工具状态和游戏加载情况观察，不要因为暂时没有后续点击就立即切换页面。

中断后，请先查看提示并处理当前问题，保持在中断对应的页面，再使用“继续”。继续功能适用于限定范围内的同页面恢复，不会自动还原任意改变过的现场。

完整操作说明见 [中英文用户手册（PDF，英文在前）](docs/user-guide-bilingual/MiliastraArchiveTool_User_Guide_EN_ZH.pdf)。手册基于较早版本整理，后续功能调整以当前程序和本 README 为准。

## 本地数据

独立解压运行时，数据通常保存在程序目录下：

```text
runtime-data/MiliastraArchiveTool/
├── archives/             # 存档、资源及各存档的 metadata.json 配置
├── logs/diagnostics/     # 诊断日志和截图
└── upload-runs/          # 上传任务运行记录
```

从源码目录运行时，程序会向上查找 `MiliastraArchiveTool.sln` 或 `.git` 目录，并使用该目录下的 `runtime-data`。升级前请备份整个 `runtime-data` 文件夹。

反馈问题时，请提供版本、游戏语言、分辨率、出现问题的步骤及对应诊断日志。分享前请检查日志和截图中的 UID、文件路径及存档内容等信息。

## 从源码构建

需要 Windows 和 .NET 8 SDK。在仓库根目录执行：

```powershell
dotnet restore MiliastraArchiveTool/MiliastraArchiveTool.csproj
dotnet build MiliastraArchiveTool/MiliastraArchiveTool.csproj -c Release
```

运行时需要项目中的 YOLO、OCR 模型和模板资源，请保留这些资源的目录结构。依赖版本以 [项目文件](MiliastraArchiveTool/MiliastraArchiveTool.csproj) 为准。

## 开源协议与致谢

本项目原创代码采用 **GNU Affero General Public License v3.0（AGPL-3.0-only）**，完整条款见 [LICENSE](LICENSE)。

项目使用 Ultralytics YOLO、ONNX Runtime、RapidOcrNet / RapidOCR、OpenCvSharp 和 Vortice 等开源组件，感谢这些项目的维护者。第三方代码、模型及其依赖保留各自的许可证和版权声明；本项目的许可证不替代这些声明。[Ultralytics 的许可说明](https://www.ultralytics.com/license)涵盖其代码与训练模型的开源许可方式。

游戏名称、截图及界面素材的相关权利归原权利人所有，不因本项目开源而转授。
