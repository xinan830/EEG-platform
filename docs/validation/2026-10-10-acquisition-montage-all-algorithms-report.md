# 采集导联全算法数值验收

本次在用户授权的真实 WPF 记录上完成独立数值复核。整段记录为 149.982 s，1000 Hz，149982 个样本；原始 EEG 为 28 路，采集导联快照输出为 21 路。**158 次 Run 的计算与独立参考一致，但这不是临床验证，也不等于真实 WPF 界面逐页人工验收。** 原始文件运行前后 SHA-256 一致。

## 验收范围

- 七个单通道算法在全部 21 路采集导联上执行整段静态分析：共 147 次。
- FAA 使用 F3-REF/F4-REF，BrainBeat 使用 Fz-REF/Pz-REF：各一次静态分析。
- 九个算法各执行一次整段动态分析：共 9 次、1298 个窗口。
- 另复核前 30 s 的 18 次静态/动态 Run，排除旧验证脚本的不等价比较。
- 全段导联投影共 3149622 个值与独立原始文件读取结果逐点相同。

动态范围请求到 149.982 s；1 s 刷新步长的末个计算端点为 149 s。不伪造尾部样本，也不强行补出不在刷新时间表中的点。默认动态窗口为 5 s；FAA 为 20 s。

本轮只新增/完善验收脚本、合成数据测试及本报告，没有修改生产算法、原始记录或 WPF 业务逻辑，也没有提交或推送。

## 结果

| 算法 | 静态 Run | 静态质量 | 动态窗口 | 独立数值复核 |
| --- | ---: | --- | --- | --- |
| PSD | 21 | 21 clean | 144 Complete / 2 Rejected | PASS |
| STFT | 21 | 15 clean / 6 partial | 144 Complete / 2 Rejected | PASS |
| RBP | 21 | 21 clean | 144 Complete / 2 Rejected | PASS |
| IAPF | 21 | 21 clean | 144 Complete / 2 Rejected | PASS |
| 频段峰频率 | 21 | 21 clean | 144 Complete / 2 Rejected | PASS |
| 频段功率比值 | 21 | 21 clean | 144 Complete / 2 Rejected | PASS |
| Theta/Beta | 21 | 21 clean | 144 Complete / 2 Rejected | PASS |
| FAA | 1 | clean | 130 Complete | PASS |
| BrainBeat | 1 | clean | 144 Complete / 2 Rejected | PASS |

部分窗口拒绝不等于 Run 计算失败。验收要求拒绝位置、质量原因与独立计算一致，而不是要求输出全部有限。

全段 Fz-REF 静态示例：IAPF 和 8-13 Hz 频段峰频率均为 11.25 Hz；Theta/Beta 为 1.6262388167260071；4-8 Hz / 13-30 Hz 频段功率比值为 1.20242265649116。FAA 为 -0.019937294840175923，BrainBeat 为 0.9964016425643499。它们与前 30 s 的值不同，因为分析范围不同，不应跨范围直接比较。

## 独立验证方法

独立参考直接解析 `samples-*.bin` 的 `<qiiq` 头与 little-endian float64 样本，读取历史导联快照中的正极和全部负极来源，先投影、再连续预处理、最后切窗。没有调用生产 Welch、频段积分、IAPF、FAA 或 STFT 计算函数，也没有把生产输出当参考输入。

PSD/RBP/STFT/FAA 本次采用 1-50 Hz Butterworth 四阶原型零相位滤波，先执行 50 Hz、Q=30 陷波。其他算法采用冻结的 1-30 Hz、关闭陷波契约；频段峰频率的 8-13 Hz 是搜索范围，不是滤波范围。参数与实际执行来源均记录在本地 JSON 报告。

Welch 使用 4 s Hann 窗、50% 重叠、2 s 步长，仅平均通过质量检查的窗口；独立实现用 SciPy periodogram 逐窗计算。FAA 保留 2 s 成对 Epoch、50% 重叠、至少 10 个有效 Epoch，比较左右 Alpha 功率的自然对数差。STFT 独立计算 4 s Hann、1 s 步长的功率矩阵，并核对线性功率、dB 换算、频率/时间轴和 NaN 掩码。IAPF 独立拟合去除 Alpha 搜索区间的 log-log 背景；Theta/Beta 使用该窗口的 IAPF 相对边界。

数值比较使用明确的 `rtol=1e-8`。PSD/线性时频功率 `atol=1e-24 V^2/Hz`，指标/占比 `atol=1e-12`，时频 dB `atol=1e-8`。同时独立核对质量计数、实际窗口、标量和 Artifact、五个占比及其总和；读取时验证 Artifact SHA-256。导联源信号另使用精确数组相等判断。

| 比较对象 | 最大绝对误差 | 最大相对误差 |
| --- | ---: | ---: |
| 全段 Fz-REF PSD，197 个频率点 | 1.6155871338926322e-27 V^2/Hz | 2.0024321913505924e-16 |
| 全段 Fz-REF STFT 有效线性功率 | 1.0339757656912846e-25 V^2/Hz | 3.743167437092108e-13 |
| 全段 Fz-REF STFT dB | 1.6200374375330284e-12 dB | 1.7836896769201098e-12 |
| 全段导联投影 | 0 V | 0 |

## 异常字段的判读

1. 旧 PSD 参考脚本把所有 Welch 窗口平均，而生产只平均有效窗口。前 30 s 中 14 个窗口有 13 个有效，因此此前约 57% 的最大相对差异是不等价输入集合造成的；修正质量筛选后误差约 2e-16。不能用“绝对值小”掩盖有意义的相对误差。
2. RBP 没有单一标量：五个值在 `band_values`/`band_share_values`。通用 `metric_value=NaN` 不等于五个占比无效。本次全部五个占比有限，拒绝的动态窗口五个值均为 null。
3. PSD/STFT 是结构化数组输出，没有 `output.value` 标量。STFT partial 表示存在拒绝的时频行，而不是矩阵没有计算出来。Fz-REF 静态 STFT 共 146 行，145 行有效、1 行拒绝；NaN 行位置与独立参考一致。
4. 0-4 s、0-5 s 的动态窗口被质量门拒绝。Fz-REF 在 1-50 Hz+50 Hz 陷波预处理后的峰值为 290.8815313055802 uV，超过 150 uV 阈值 140.8815313055802 uV；没有缺样或 NaN 输入。不能仅据此推断原始数据损坏或患者异常。
5. 全段 Fz-REF PSD 有 72/73 个有效 Welch 窗，质量门通过。因此静态 clean 表示整体可用，不表示每一个原始时刻都没有伪迹。
6. 动态指标顶层 `output` 来自首个窗口；判断整段与当前点必须读取 `series`/`windows`。RBP 拒绝窗口的详细质量字段位于 `failure.detail`，不应只读空的 `source_quality`。

## 证据与复现

采用通用工程验收报告结构，不使用安全事件/临床报告模板。证据只保留在本机；未复制或提交真实 EEG、姓名、原始信号数组。

| 证据 | 路径 | SHA-256 |
| --- | --- | --- |
| E1：158 次全通道运行及逐输出误差 | [all_channels_all_algorithms.json](../../_verify/all_channels_all_algorithms.json) | 4b26173511ef064cee143b76adb2ead3fa2fbfa8568837c84bc4856e559f32cf |
| E2：18 次默认通道全段运行 | [all_algorithms_full_recording.json](../../_verify/all_algorithms_full_recording.json) | 094f656ca5afc4b328f381fbe841dbbec25a4d79e54b85098073ae2076f5a981 |
| E3：18 次前 30 s 复核 | [all_algorithms_30s_reference.json](../../_verify/all_algorithms_30s_reference.json) | 96945c39f4bc30abe1f108f80a755a38fdec315d757116184c15a8deec218362 |

E1/E2 支持本次“默认角色动态及全部导联静态数值一致”的结论；E3 与旧参考脚本解释 PSD 差异来源。数据流为：原始文件/采集快照 -> 导联投影 -> 连续预处理 -> 质量筛选 -> 算法 -> Run/Artifact -> 独立比较。临时目录中的 Run 数据库和完整 Artifact 验证后自动清理，报告保留比较结果、参数和校验摘要。

在仓库根目录运行；输入已经获得授权的记录目录，不把个人路径写入版本库：

```powershell
$taskRecordingDirectory = Read-Host '输入已授权的 WPF 记录目录'
.\backend\.venv\Scripts\python.exe _verify\run_all_algorithms_real_recording.py $taskRecordingDirectory --all-static-channels --output _verify\all_channels_all_algorithms.json
.\backend\.venv\Scripts\python.exe -m pytest _verify\test_real_recording_reference.py -q
Push-Location backend
.\.venv\Scripts\python.exe -m pytest -q
Pop-Location
Push-Location desktop-client
dotnet test BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore
Pop-Location
```

本轮独立验收工具合成测试 11 passed，后端 479 passed，WPF Release 自动测试 448 passed。`analyze-acquisition-montage-signals`、`extend-official-rbp-to-gamma` 两项 OpenSpec strict 校验及 `git diff --check` 通过。后端存在依赖弃用警告；WPF 存在旧 OpenTK/SkiaSharp 包的 NU1701 兼容性警告，测试通过不代表这些警告已经消除。

环境为 Python 3.13.3、NumPy 2.5.3、SciPy 1.18.1。仓库 HEAD 为 `752ea3dfbafae9b301010e5da2a80ce250e675a6`，执行包含工作区现有未提交变更，不能把结论当成仅针对该提交的验证。

## 尚未覆盖的范围

本次没有把九个动态算法在全部 21 个通道/所有双通道组合上穷举，没有穷举所有滤波参数、采样率、设备及缺样记录，也未通过真实 WPF 点击逐页验证图表。当前记录使用 REF 导联；其他参考形式主要由已有单测及本轮合成双极导联测试覆盖。

RBP 五频段遵循 `extend-official-rbp-to-gamma` 的版本化变更，不应套用尚未归档合并的旧四频段规格。BrainBeat 保留当前冻结的网格积分和 epsilon 语义；本次是实现数值一致性检查，不是重新论证该指标的医学意义。记录开头的幅度超阈值可能涉及实际信号与滤波边界，应另行研究，不能通过放宽阈值伪造有效数据。
