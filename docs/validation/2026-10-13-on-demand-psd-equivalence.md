# 按需 PSD 科学等价验证（2026-10-13）

本文件保留最初原语 fixture 的验证范围。后续真实 RecordingService 文件链路、
114 窗口完整 evidence 对照、小时资源及全量回归见
[当前验证报告](2026-10-13-on-demand-psd-validation-report.md)；不再把以下早期缺口当作当前状态。

## 验证范围

使用脱敏、确定性的 20 秒单通道合成源（200 Hz，10 Hz 与 6 Hz 正弦叠加），先
对整段源执行当前连续零相位 `1–30 Hz` 预处理，再由窗口加载器切片并执行现有
Welch 合同。没有使用真实 EEG，也没有把该 fixture 写入仓库之外的 artifact。

## 对照项

同一配置（动态 PSD、5 秒窗口、1 秒步进、输出 1–50 Hz）分别运行：

1. 既有 `execute_dynamic` 全窗口路径；
2. 按需 `plan_window` + `execute_window`，按每个完整窗口末端游标请求。

逐窗口检查：

- 半开 sample bounds 和计划时间范围；
- 频率轴逐点相等；
- PSD 数组 `rtol=1e-12, atol=1e-24`；
- 频段功率占比 `rtol=1e-12, atol=1e-12`；
- `V^2/Hz`、`ratio` 单位；
- clean quality、source quality evidence 和连续预处理 provenance。

## 结果

`tests/test_psd_on_demand_capability.py` 中的 6 项 PSD 能力测试全部通过，包含上述
连续预处理 source fixture、首窗/后续窗边界、质量拒绝、固定轴和取消检查。

这份早期证据只证明窗口适配器在该科学 fixture 上与完整动态路径一致。
后续报告已补齐真实文件链路、gap/尾窗/非零计数器专项与服务阶段耗时；GUI 播放仍待 P3 验收。
