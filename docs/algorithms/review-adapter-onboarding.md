# 算法回溯适配器接入清单

更新日期：2026-10-22。算法列表/详情中的 Run 接入不自动意味着记录回溯已接入。回溯必须逐项实现桌面 adapter 和 presenter，并通过窗口数据、状态、质量、provenance 与用户操作验收。当前仅 PSD 已注册到记录回溯；其余算法虽然有后端按需窗口实现，仍不可在 WPF 回溯中选择。

## 固定边界

- `ReviewAnalysisCoordinator` 管游标目标、播放/拖拽触发、取消订阅和过期结果隔离；不含算法 ID 分支、科学公式或输出数组语义。
- 每个算法 adapter 负责 immutable config/snapshot 到请求的映射、该算法响应身份校验和 typed 输出投影；presenter 负责单位、轴、状态/质量与证据的呈现。
- 后端模块拥有窗口规划、连续预处理、缺样处理、公式、质量门和真实范围/provenance。桌面端不从全量序列伪造按需窗口、不重算、不为不支持能力回退到逐游标 Run。
- 只有后端窗口实现、adapter、presenter、科学等价证据和桌面行为验收全部通过，才可注册。目录 `is_runnable` 或后端 `execute_window` 存在都不足以开放回溯。
- 按需窗口 API 返回规范 `scientific_configuration_json` 及其 `configuration_sha256`。桌面端按原始 UTF-8 JSON 字节校验摘要，再验证请求配置与后端规范化配置一致后绑定快照；桌面端不得自行重建科学上下文或摘要。

## 九算法后端能力与桌面接入状态

所有后端项来自当前 builtin manifest/config/window 实现；算法间共用 `channel`, `mode`, `start_s`, `end_s`, `window_s`, `step_s` 动态字段（按各模式适用），下表只列算法私有字段。

| ID | 后端单窗/窗口策略身份 | 输入与连续预处理 | 算法私有请求配置 | 输出形状/单位 | 质量与证据投影要求 | WPF 回溯 |
| --- | --- | --- | --- | --- | --- | --- |
| PSD | `psd-dynamic-window-v1` | 单通道 spectrum；配置的连续预处理后切窗；保留 NaN | `low_hz`, `high_hz`, `notch_hz` | 频率轴 Hz、PSD V²/Hz、5 项 band share ratio | quality/state；Welch 参数、band share、sample coordinate；保持频率网格和数组轴 | **已注册** |
| STFT | `stft-dynamic-window-v1` | 单通道 spectrogram；配置的连续预处理后切窗；保留 NaN | `low_hz`, `high_hz`, `notch_hz` | 内层 time×frequency；时间 s、频率 Hz、线性 V²/Hz、dB re 1 µV²/Hz | quality/state；契约版本、有效内层点数、padding policy；校验两个轴与矩阵形状 | 未注册 |
| RBP | `rbp-dynamic-window-v1` | 单通道 spectrum；固定连续预处理后切窗；保留 NaN | 无 | Delta/Theta/Alpha/Beta/Gamma 五路 ratio | denominator、各 band power、频带、window；保留五路和每路状态，不压成单 scalar | 未注册 |
| Band Ratio | `scalar-dynamic-window-v1` | 单通道 spectrum；模块声明预处理；保留 NaN | numerator/denominator 的 low/high Hz | ratio | 正分母质量门、比值证据、sample coordinate；失败不能伪装为 0 | 未注册 |
| Peak Frequency | `scalar-dynamic-window-v1` | 单通道 spectrum；模块声明预处理；保留 NaN | `low_hz`, `high_hz` | peak frequency Hz | 搜索频带、peak evidence、sample coordinate；保留频点规则和失败原因 | 未注册 |
| IAPF | `scalar-dynamic-window-v1` | 单通道 spectrum；模块声明预处理；保留 NaN | 无 | IAPF Hz | IAPF evidence、peak Hz、COG Hz、estimate source；呈现质量拒绝/不可用原因 | 未注册 |
| Theta/Beta | `scalar-dynamic-window-v1` | 单通道 spectrum；模块声明预处理；保留 NaN | 无 | dimensionless ratio | IAPF Hz、派生 Theta/Beta 范围和 ratio evidence；不要把该指标当固定频带配置 | 未注册 |
| FAA | `faa-dynamic-window-v1` | 按角色顺序 F3/F4 paired time series；配置的连续预处理后切窗；保留 NaN | `f4_channel`, `low_hz`, `high_hz`, `notch_hz`（`channel` 是 F3） | FAA dimensionless | paired epoch quality、最低干净 epoch 门、preprocessing range、FAA evidence；任何角色/范围不完整均须拒绝展示数值 | 未注册 |
| Brainbeat | `brainbeat-dynamic-window-v1` | 按角色顺序 Fz/Pz paired spectrum；配置的连续预处理后切窗；保留 NaN | `pz_channel` | Brainbeat ratio | paired role order、window source、Brainbeat evidence；独立窗口结果，不引入跨窗 EMA | 未注册 |

上表中 `execute_window` 已实现不代表桌面支持，也不代表任意真实记录、配置与边界均已做科学验证。质量的公共状态词表为 `clean`, `gate_failed`, `unavailable`, `insufficient_data`, `partial`，动态状态为 `Partial`, `Complete`, `Rejected`, `Unavailable`；各模块专属 quality rule 和 evidence 仍由各自 presenter 解释。

## 每个 adapter 的接入验收

1. 从当前 manifest/config/window 固定完整配置字段、角色次序、策略版本、输入表示、连续预处理范围、缺样行为及允许窗口范围；禁止根据 UI 显示通道名猜测信号角色。
2. 实现 adapter：冻结 recording ID/source checksum、目标整数 sample、目录定义/科学/实现身份及完整模块配置；把 API 返回身份与窗口实际范围逐项校验。未知或缺失身份按失败处理。
3. 实现 typed presenter：核对轴、单位、数组形状、窗口起止、质量/状态、失败原因和算法专属 evidence；状态不是有效数值的响应不得渲染成成功值。
4. 使用独立窗口参考或既有冻结数值验证至少首窗、中间窗、尾窗、缺样/拒绝状态和切换目标；与 Run/全量序列比较时仅比较同一科学样本范围、同配置和相同算法版本。
5. 自动化覆盖播放每秒观察、拖拽 release、同窗去重、反向 seek、快速替换、关闭/切换记录、取消和异常；手动 WPF 验收需实际检查结果范围与 provenance。
6. 验收通过后才加入显式 registry，并同步本表、架构文档、OpenSpec tasks 和验证证据。未完成保持 unavailable；不使用全量结果临时适配，也不添加通用 JSON fallback。
