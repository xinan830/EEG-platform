# 实现地图与风险清单

## 关键代码

- 文件读取与窗口编排：`backend/app/services/recordings.py`
- 回放会话：`backend/app/services/waveform_playback.py`
- 显示滤波：`backend/app/services/waveform_filter.py`
- Montage：`backend/app/services/montage.py`
- API 模型：`backend/app/models/montage.py`
- 离线预处理与 PSD：`backend/app/eeg_core/spectral.py`
- IAPF 与指标：`backend/app/eeg_core/offline_metrics.py`
- FAA：`backend/app/eeg_core/faa.py`
- 算法参数：`backend/app/eeg_core/analysis_contract.py`

## 已知风险

1. `custom_bipolar` ID 名称历史遗留，后续可迁移为 `custom_linear`，需兼容旧客户端。
2. 显示因果滤波与离线零相位滤波数值不应混比。
3. AVG 排除集合若未随结果返回，用户无法复现数值；API 应持续返回完整设置快照。
4. 当前算法版本是工程验证版，不是临床验证或合规声明。
5. 400 行是软上限；超过阈值按 `docs/code-size-policy.json` 登记职责评估，不机械拆分。

## 变更要求

算法参数、调用顺序、单位、参考集合或质量门发生变化时，必须新增 `docs/changes/YYYY-MM-DD-*.md`，同步测试和版本号。
