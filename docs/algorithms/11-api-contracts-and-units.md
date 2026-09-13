# API、版本与单位契约

## 可配置请求

`POST /api/recordings/{recording_id}/spectrum/configured` 与 `/spectrogram/configured` 使用同一请求模型：

```json
{
  "mode": "static",
  "channels": ["F3", "Fz", "Pz"],
  "time": { "start_s": 10.0, "end_s": 40.0 },
  "dynamic_window_s": 10,
  "refresh_step_s": 1
}
```

`mode` 可为 `static`、`dynamic`、`spectrogram`；通道非空、去首尾空格后不得重复；区间为 4–120 s。动态窗口只允许 5/10/20/30 s，刷新步长只允许 1/2/5 s。`custom_frequency_range` 仅用于 spectrogram，且必须在 1–30 Hz 内、上限大于下限。

## 版本字段

| 字段 | 含义 |
|---|---|
| `analysis_algorithm_version` | 滤波、PSD、Band Power、RBP 数学基线，当前为 `offline-spectral-v3` |
| `spectrogram_contract_version` | 时频时间轴、矩阵、单位和质量语义，当前为 `spectrogram-v2` |
| `algorithm_version` | 当前端点/配置包装版本，例如 `offline-spectral-v4-configurable` 或 `spectrogram-v2-configurable` |
| `analysis_config_hash` | 规范化请求配置 SHA-256 的前 12 位大写摘要，用于追踪，不是数据文件哈希 |

配置端点返回 `requested_config` 和 `execution_config`，并回显 requested/actual 时间范围、采样率、通道顺序、算法参数和质量统计。调用方应展示 actual range，不能假设请求范围一定原样执行。

## 单位

| 数据 | 内部单位 | API/显示单位 |
|---|---|---|
| 原始与预处理 EEG | V | 波形 `uV` |
| PSD density | `V^2/Hz` | `uV^2/Hz` |
| 频段积分功率 | `V^2` | `uV^2` |
| RBP | 比值 | 0–1，界面可格式化为 % |
| Spectrogram dB | 不作为计算主值 | `dB re 1 uV^2/Hz` |
| FAA | 对数功率差 | 无量纲 |

单位转换只在 API 边界执行一次：`V → uV` 乘 `10^6`，`V^2 → uV^2` 乘 `10^12`。前端只格式化，不重算 EEG 数学结果。

## 错误与边界

静态范围越过文件末尾返回结构化 422，不应默默扩展或把不足区间报成完整区间。误差不超过一个采样周期的末端请求可按实际样本边界容忍并回显 actual range。错误响应依靠稳定 `code` 和 `request_id`，前端不得解析中文错误文本做控制流。
