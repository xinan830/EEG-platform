# 回溯局部波形消失修复验证

完整应用的播放与参数切回仍需用户验收。本报告证明了同一份真实滤波数据在实际
SciChart 绘图探针中的修复前后差异，不把离屏截图等同于完整界面验收。

## 结论与范围

问题发生在回溯图表拼接源块的显示层，不是 F8 通道映射或记录丢样。
旧代码在连续的 10 秒滤波块之间插入 `X=NaN, Y=NaN`，破坏图表时间坐标。
在这份数据上，SciChart 的实际绘制跳过了 F8-REF 的前一块，而后一块仍可见。

修复仅涉及回溯显示投影：相邻块不增加断线，真实样本缺口和无效信号使用
有限的记录秒坐标及 `Y=NaN`。显式采用 `LineDrawMode.Gaps`，不插值补样。
原始文件、因果滤波、导联公式、分析计算、缓存格式及许可证均未修改。
工作区已有的窗口跟随和动态算法改动保持原样。

## 证据链

| 证据 | 发现 | 修复位置 |
| --- | --- | --- |
| 记录 `8e0c630a60d14913ae9f5c60d7b23edf`，1000 Hz，149.982 s；F8 原始值均有限 | 不支持原始数据丢样解释 | 读取链路不修改 |
| 检查 63 个已有缓存片段，无 NaN/Inf；默认 1/30/50 Hz 前 20 s 的 21 条 REF 与新滤波结果最大差值为 0 V | 不支持滤波缓存损坏解释 | 滤波和缓存不修改 |
| 修复前真实数据图表包含 1 个 NaN X，F8 前 10 s 消失；新增 4 项测试全部失败 | 连续块间的非有限时间标记是可复现的绘图缺陷 | `RecordingReviewSciChartCanvas.UpdateSeries` |
| 修复后同一数据 F8 全段恢复，NaN X 数为 0；增补后 7 项专项测试通过 | 有限时间坐标及真实缺口规则解决此复现 | `ReviewWaveformTraceProjection` |

缓存与新滤波比较属于重复计算一致性检查，不代表独立临床或算法有效性验证。

## 自动验证

- WPF 回溯及 `MontageDisplayProjector` 相关测试：152 项通过。
- 后端显示滤波契约、服务、接口与检查点测试：11 项通过；2 项依赖弃用警告。
- WPF Release 构建：0 警告、0 错误。
- `openspec validate fix-review-waveform-chunk-gaps --strict`：通过。
- `git diff --check`：通过，工作区有 LF/CRLF 提示，无空白错误。

七项专项覆盖：连续块、真实计数器缺口、NaN、Inf、极值时间顺序与单位转换、
非零样本时间原点、缓存数组切回的确定性及原值不变。

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --filter "FullyQualifiedName~Review|FullyQualifiedName~MontageDisplayProjector" --artifacts-path _verify/review-waveform-fix -p:NoWarn=NU1701 --verbosity quiet
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --configuration Release --artifacts-path _verify/review-waveform-release -p:NoWarn=NU1701 --verbosity quiet
```

后端在 `backend` 目录执行：

```powershell
.\.venv\Scripts\python.exe -m pytest tests/test_filter_contract_regression.py tests/test_live_filter_service.py tests/test_live_filter_api.py tests/test_filter_checkpoint_cache.py -q
```

## 实际绘图检查

离屏 WPF 窗口加载真实缓存前两块、21 条 REF，使用正式回溯 Canvas 与 SciChart，
保留加速绘制配置。窗口宽度 800/1600 DIP；时间视窗为 11 s，起点分别为
0/0.25/1/5 s。八张截图均显示 F8，前部测试区的波形像素列覆盖率为
99.92%–100%，没有原先整块空白。抗锯齿允许少量单列无像素。

本地探针源位于 `_verify/review-waveform-probe`，截图位于
`_verify/review-waveform-before` 与 `_verify/review-waveform-after`。
这些本地验证产物不提交；真实 EEG 样本和截图不进入版本库。

## 用户复测

1. 关闭目前正在运行的旧进程，重新编译启动 WPF。
2. 打开同一记录，选择官方 REF 导联，设置高通 1 Hz、低通 30 Hz、陷波 50 Hz。
3. 从开头播放至游标到达中心并继续滚动，确认 F8-REF 在 10 s 之前也有波形。
4. 把一个滤波参数改变再恢复，确认原参数下波形仍显示。
5. 向前、向后定位；有真实缺样的记录仍应保留空白。

无需删除缓存或重采集。已提取独立显示投影职责；Canvas 从本轮修改前的
531 行降至 467 行，规模评估见 `docs/code-size-policy.json`。
