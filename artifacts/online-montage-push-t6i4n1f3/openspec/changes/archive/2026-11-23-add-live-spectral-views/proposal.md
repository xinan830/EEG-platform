## Why

采集时需要查看时频图、功率谱密度以及 RBP 和 PSD 头皮地形图。回溯已有对应的科学计算和图表，可以复用。

## What Changes

- 在现有实时分析协调器注册 PSD、STFT 和两种地形图视图。
- 在采集页增加实时频谱面板，沿用回溯的图表控件。
- 使用当前采集会话的滚动窗口。结果仅在采集会话中展示。

## Capabilities

### New Capabilities

- `analysis/live-spectral-views`: 实时 PSD、STFT 及 RBP、PSD 地形图。

### Modified Capabilities

## Impact

涉及桌面采集面板与后端实时算法注册。沿用现有 HTTP 封装，通过算法 ID 和 payload schema 区分结果。单位、时间和通道顺序明确。无需数据库迁移。
