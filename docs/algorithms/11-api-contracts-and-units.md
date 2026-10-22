# 算法 API、版本与单位

## 入口分工

| 入口 | 责任 |
| --- | --- |
| `GET /api/algorithms` | 当前算法身份、参数与能力目录 |
| `POST /api/runs` | 持久化并排队执行完整 Run |
| `GET /api/runs/{run_id}` | 生命周期与结果摘要 |
| `GET /api/runs/{run_id}/result` | 经当前格式校验的结果视图 |
| `POST /api/analysis-windows/ensure` | 按游标及模块能力获取单窗 |
| `POST /api/recordings/{recording_id}/spectrum/configured`、`/spectrogram/configured` | 记录级可配置谱端点；不是九算法 Run 的统一请求 schema |

请求字段和约束以当前 Pydantic schema、模块 config 与 OpenAPI 为准，不复制一份可能漂移的通用 JSON 模板。动态窗口规则不能从某个记录级谱端点推导到所有算法。

## 身份与格式

科学版本、模块实现身份、Definition 不可变版本/digest、处理契约和结果格式是不同身份。配置 hash 不能代替源文件 checksum。官方版本由显式 current 解析，不按版本字符串猜“最新”。

完整官方结果为 `official-algorithm-result-v1`；标量动态为 `dynamic-analysis-frame-v6`。只消费当前 canonical 输出和显式状态，不解析退休格式。不合法的已完成官方结果按 `ALGORITHM_RESULT_INVALID` 失败，不自动转换。

## 单位

| 数据 | 契约 |
| --- | --- |
| 时序算法输入 | V/float64，samples × channels |
| 波形 API/显示 | 显式声明 µV，不能再重复乘 10⁶ |
| 科学 PSD / band power | V²/Hz / V²；服务或展示可能声明 µV²/Hz / µV² |
| RBP、比值、FAA | 按模块声明的无量纲值；百分比只是格式化 |
| 频率、采样率 | Hz |
| 时频 dB | 显式参考值，如 dB re 1 µV²/Hz |

换算只在有声明的边界做一次：V→µV 为 10⁶，功率单位为 10¹²。不能因所有字段都来自 API 就假定单位相同。范围/通道/轴顺序随结果保留，不可用科学值用 null 与原因表示。

HTTP 状态码、错误 code、request_id 与业务状态分别消费；不按中文消息做控制流。边界与取整规则按各请求契约精确核对，不添加通用的“一样本误差忽略”规则。
