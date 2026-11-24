# 记录时间切换验收记录

已实现公共 `记录时间 / 采集时间` 显示切换。真实记录的窗口/GPU验收尚需用户完成；自动化测试不代表外部时钟同步或设备验证。

## 如何验收

重新编译并启动 WPF，打开一条完成记录：

1. 在回溯底部切换两种时间，检查波形轴、导航刻度、事件标签及游标文字同步改变。
2. 定位到 17 秒并播放，切换时间后检查位置、播放状态和导联不变，没有重新加载或分析。
3. 打开该记录的算法详情，在顶部切换时间；检查动态游标、当前窗口、结果范围和趋势/时频时间轴。
4. 检查分析范围输入仍标注“记录秒数”，输入 38–45 后提交的 `start_s/end_s` 仍为 38/45。
5. 在 STFT 的时间趋势和频谱切片、RBP 的时间趋势和频段图之间切换；频率/频段轴不能变成时钟。
6. 小窗口及跨午夜情况下检查文字换行、进度条可用、日期显示；没有采集起点的记录应禁用采集时间。
7. 暂停在固定位置，反复切换两种时间，检查网格、波形和事件线不移动。起点为 `10:47:21.014` 时，`0s`、`1s` 应分别标为 `10:47:21.014`、`10:47:22.014`，不能对齐到 `10:47:22.000`。

切换设置在当前工作站会话共享，重新启动应用默认回到记录时间。

## 模块与依据

| 模块 | 依据与职责 | 不允许的副作用 |
| --- | --- | --- |
| `Shared/Time/RecordingTimeDisplayContext` | 明确起点 + 既有 `RecordingTimeMapper` 换算；午夜、缺失起点有测试 | 不解析显示字符串、不猜测创建时间 |
| `Shared/Controls/RecordingTimeSelector` | 两段切换、禁用态；180/220 DIP 布局及渲染测试 | 不提交分析请求 |
| `Shared/Waveform/RecordingTimeAxisPresentation` | 只接管显式注册的时间轴；保留坐标、数值与 NaN | 不移动范围、不重建数据系列 |
| 回溯 `TimeDisplay` partial | 格式通知、事件文字原位更新；17 秒播放/取数回归测试 | 不丢失选中事件、不重置播放 |
| 项目目录与算法公共上下文 | 传递已读取的 UTC 起点、公共范围/游标格式 | 不改变原始记录或算法参数 |

算法中央接入只增加工作站共享设置注入、项目记录行的可选 UTC 字段及详情页时间上下文。
算法图表仍独立，时间图采用同一格式化适配器。科学诊断文本保留其明确的记录秒数，不重写历史证据。

## 自动化结果

- 全部桌面测试：484 通过，0 失败，0 跳过（含本次小数秒起点回归）。
- Release 构建：通过；验证命令仅抑制已有依赖兼容警告 `NU1701`。
- OpenSpec `add-recording-time-display-switch --strict`：通过。
- `git diff --check`：通过。
- 后端/网页文件规模脚本通过；本次涉及的超 400 行桌面文件已登记评估。时间显示新增职责在专用公共模块/partial 中。
- 公共切换控件已离屏渲染检查，动态状态卡在 440/620 DIP 下的长日期和完整进度条布局通过测试。
- STFT/RBP 图表类型切换、共享偏好、缺失起点、午夜/长时长、不可用数值、原数据/范围不变都有回归测试。
- 未改后端科学计算/API，没有另行声称后端数值重验证。未关闭用户正在运行的 WPF 进程。

## 小数秒起点偏移修复

证据：回溯 `UpdateTimePresentation` 原先在采集时间模式把刻度起点改为真实 UTC 起点。
`WallClockSecondTickProvider` 随之扣除起点的小数秒；起点含 `.014` 时，第一条采集整秒网格位于 `0.986s`，不是记录的 `1s`。
`FormatAxis` 又仅显示到秒，掩盖了这项对齐差异。

结论：这是显示模式切换改变网格坐标的缺陷，不是数据移动或采样时间错误。
修复路径：回溯刻度始终按记录整秒计算，仅用共享时间上下文转换标签；`FormatAxis` 使用 `HH:mm:ss.fff`。
实时采集的整秒刻度策略及旧标签提供器的兼容路径未改变。

新增/加强测试先在旧实现上复现了 6 项失败，修复后全部通过：

- `.014`、`.387`、`.999` 起点下，真实回溯控件反复切换模式，刻度仍为记录的 `1/2/3s`，视窗对象、位置和取数次数不变。
- 毫秒格式、半秒位置及午夜日期正确；既有播放中切换测试保持通过。
- 640/1280 DIP 离屏布局中，真实网格面板已布局，切换后的绘图区矩形与之前一致。这不替代用户真实窗口/GPU验收。

显示到毫秒只表示格式精度，不证明设备与外部时钟达到毫秒级同步。
隔离构建不会更新正在运行的 WPF；用户需重新编译启动后执行上述验收。

## 复现命令

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --artifacts-path _verify/time-display-build --verbosity quiet -p:NoWarn=NU1701
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --configuration Release --artifacts-path _verify/time-display-build --verbosity quiet -p:NoWarn=NU1701
openspec validate add-recording-time-display-switch --strict
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
git diff --check
```

使用独立构建输出，避免覆盖正在运行的客户端。原始 EEG、manifest、历史 Run 与缓存身份均不受本次显示切换影响。
