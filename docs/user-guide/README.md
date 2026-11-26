# EEG 科研平台使用手册

> 面向平台使用者的操作说明。建议第一次使用先读“快速上手”和“项目、受试者与记录”，再按任务进入采集、导入或回溯章节。每篇说明一项工作流，避免把所有操作挤在一个长文中。

## 文档导航

### 开始使用

- [快速上手](getting-started/quick-start.md)：从项目进入采集或导入记录的基本路径。
- [数据与界面术语](getting-started/data-basics.md)：项目、记录、通道、导联、时间和信号单位的含义。
- [系统功能地图](feature-map.md)：各页面用途和功能边界。

### 功能操作

- [项目、受试者与记录](features/projects-subjects-recordings.md)
- [实时采集](features/acquisition.md)
- [通道与导联配置](features/channels-and-montages.md)
- [EDF/BDF 导入与导出](features/edf-bdf-import-export.md)
- [数据回溯与波形浏览](features/review-and-playback.md)
- [科学预处理与 ICA 审查](features/scientific-preprocessing.md)
- [算法分析与结果查看](features/algorithms-and-results.md)
- [3D 脑影像与空间可视化](features/brain-imaging-3d.md)
- [常见问题与排查](troubleshooting/common-issues.md)

## 阅读说明

- 本手册描述桌面客户端当前可见的操作路径；后端科学行为和公共数据契约仍以项目规范及代码为准。
- “截图待补”是预留位置。截图请放在 `screenshots/` 中，并使用条目建议的文件名，再把占位提示替换为 Markdown 图片。
- 截图使用真实界面即可；请先遮挡姓名、受试者编号、文件路径、设备序列号等身份信息，也不要把真实 EEG 波形或日志上传到公共位置。
- 当前手册根据桌面端页面和控件核对编写。未在界面中确认的能力不会作为已开放功能承诺。

## 按任务查找

| 你现在要做什么 | 从哪里开始 | 后续阅读 |
|---|---|---|
| 第一次打开系统，不知道先做什么 | [快速上手](getting-started/quick-start.md) | [系统功能地图](feature-map.md) |
| 建项目、录入或修改受试者 | [项目、受试者与记录](features/projects-subjects-recordings.md) | [EDF/BDF 导入与导出](features/edf-bdf-import-export.md) |
| 核对硬件输入和电极映射 | [通道与导联配置](features/channels-and-montages.md) | [实时采集](features/acquisition.md) |
| 采集新 EEG | [实时采集](features/acquisition.md) | [科学预处理](features/scientific-preprocessing.md) |
| 导入、导出 EDF/BDF | [EDF/BDF 导入与导出](features/edf-bdf-import-export.md) | [数据回溯](features/review-and-playback.md) |
| 查看波形、事件或分析历史记录 | [数据回溯与波形浏览](features/review-and-playback.md) | [算法分析与结果](features/algorithms-and-results.md) |
| 查看坏导、坏段、ICA 处理 | [科学预处理与 ICA 审查](features/scientific-preprocessing.md) | [算法分析与结果](features/algorithms-and-results.md) |
| 浏览三维脑电地形、脑网络、源定位或结构影像 | [3D 脑影像与空间可视化](features/brain-imaging-3d.md) | [通道与导联配置](features/channels-and-montages.md) |
| 操作失败或入口不可用 | [常见问题与排查](troubleshooting/common-issues.md) | 记录错误码并联系维护人员 |

## 手册边界

本手册告诉你在当前桌面客户端中如何找到入口、按什么顺序操作以及怎样核对结果。它不能替代设备说明书、实验方案、研究伦理要求或 EEG 专业判断。硬件量程、参考选择、坏导处理阈值和 ICA 成分选择都应结合实验设计和数据质量确认。

界面会随版本变化。如果按钮名称与当前安装版本不同，优先按同一区域的用途辨认，并记录软件版本/提交号反馈；不要因为旧版截图而盲目执行不可逆操作。

## 截图规范

请参阅[截图目录说明](screenshots/README.md)。
