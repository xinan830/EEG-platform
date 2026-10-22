# 可追溯分析底座

> 当前边界：本地单用户工作台。标准 `/api/runs` 使用 SQLite 持久队列。官方算法结果只支持当前格式，详见 [当前结果契约验证](../validation/current-algorithm-result-contract-validation.md)；实现边界与新增算法步骤见 [官方算法接入指南](../algorithms/official-algorithm-development.md)。

## 追溯一个结果

每个新分析通过 `AnalysisRun` 形成以下链路：

```text
Recording source SHA-256
  -> scientific definition/version
  -> normalized configuration SHA-256
  -> requested and actual absolute range
  -> ordered channel mapping/reference/filter/window/quality rules
  -> implementation build and runtime environment
  -> immutable NPZ artifact SHA-256
  -> result summary or structured failure
```

官方算法通过 `/api/runs` 执行；项目已无 `/api/recordings/{id}/analysis` 与 `GET /api/analyses/{id}` 旧分析路由。记录的 `/spectrum/configured` 和 `/spectrogram/configured` 仍是独立的配置频谱接口，不承担旧官方结果转换。现有 Run/artifact 字节不迁移、不删除，也不把 Viewer 的 montage/filter 隐式带入 Analysis。当前官方结果必须通过版本、输出结构和显式状态校验后才能读取或导出。

## 数据库升级

当前数据库结构以 `backend/app/persistence/migrations.py` 的迁移序列为准。它覆盖 recording/source 身份、Run/队列、Definition、artifact、ValidationRun 和导联快照等存储边界；数据库 schema 版本、算法科学版本与结果契约版本是不同身份，不互相替代。

迁移按版本逐个执行，每个版本使用独立 `BEGIN IMMEDIATE` 事务。版本号只在同一事务末尾推进；失败会回滚该版本。重复启动不会重复新增表、列或记录。

旧 Recording 若源文件仍存在，会补算 SHA-256 和字节数。若文件已经缺失，旧元数据保持可读，`source_sha256` 和 `file_size_bytes` 为 `null`，不能创建要求可追溯源身份的新 Run。

## Recording 身份

新导入保存：

- `source_sha256`：实际落盘源字节的 SHA-256；
- `file_size_bytes`：实际字节数；
- `raw_channel_labels`：文件原始顺序和文本；
- `canonical_channel_labels`：确定性去首尾/重复空白后的标签；
- `channel_types` 和 `channel_units`：与通道位置对齐；
- `import_version = recording-import-v1`。

规范标签仅用于身份和匹配，不允许把两个源通道静默合并。源 EEG 文件只读。

## 创建和读取 Run

创建一个 10 秒静态 Peak Frequency Run（通道名必须是所选分析信号中实际存在的输出）：

```http
POST /api/runs
Content-Type: application/json

{
  "recording_id": "<recording-id>",
  "analysis_type": "official_algorithm",
  "config": {
    "mode": "static",
    "algorithm_id": "peak_frequency",
    "channel": "Oz",
    "time": {"start_s": 10.0, "end_s": 20.0}
  }
}
```

标准接口入持久队列并返回 HTTP 202、稳定 Run ID 和当前状态；客户端轮询 `GET /api/runs/{run_id}`，不能把创建响应视为计算完成。命中复用时可以直接得到已有结果。生命周期状态包括：

```text
queued -> running -> completed | gate_failed | failed
queued -> cancelled
running -> cancelled
```

查询资源：

```text
GET  /api/runs?recording_id=<id>&limit=100
GET  /api/runs/{run_id}
POST /api/runs/{run_id}/cancel
GET  /api/runs/{run_id}/artifacts
```

排队任务可以取消；运行中的任务由 worker 在执行检查点协作取消，不能承诺即时中断底层数值调用。终态取消返回 HTTP 409、`RUN_NOT_CANCELLABLE`，不重写已完成结果。按需窗口有自己的任务生命周期；combined 模式分别管理预览与后台全量的状态和取消。

## 缓存身份

规范 JSON 使用 UTF-8、对象键排序、紧凑分隔符、保留数组顺序、拒绝 NaN/Infinity。Run 与窗口缓存必须区分以下身份：

```text
source file SHA-256
+ algorithm definition SHA-256
+ resolved configuration SHA-256
+ implementation build identity
+ actual absolute time range
+ current result contract
+ selected montage snapshot and ordered signal outputs
+ declared preprocessing/filter/notch contract
```

即使科学定义版本不变，只要 PATCH 修复可能改变数值，也必须改变 `BRAIN_PLATFORM_BUILD_VERSION` 或包构建版本，从而生成新的缓存身份。

连续预处理缓存拥有整段准备数据，身份包含源通道顺序/单位、采样率、导联映射、预处理作用域和实现身份；具体窗口样本边界在 Run/window identity 中管理。改变显示历史长度只影响界面，不改变计算身份。仅在模块声明支持的复用方向且完整身份匹配时复用。

## 算法输入与结果边界

模块通过 manifest 的 `SignalRequirements` 声明输入角色、单/配对输入及预处理范围。`_RecordingAlgorithmContext` 校验强类型 source request 的导联身份、角色、采样率与绝对样本边界，再调用现有读取/投影/连续预处理服务。时序输入为 `(samples, channels)` 的 V/float64；频谱适配显式将服务的 uV²/Hz 转为计算所需的 V²/Hz。缺失样本保留为 NaN 或按声明拒绝，禁止补零或替换通道。

FAA 的配对连续预处理和 Brainbeat 的双角色逐窗频谱保留各自语义，不能因共享入口而强制统一滤波或公式。科学计算和专属质量规则归算法包；Runtime 校验模块输出；通用 serializer 只按 scalar、series、structured、structured-series 四种类型处理。数组交给 artifact 服务持久化，WPF 只展示后端数值。

官方结果根版本为 `official-algorithm-result-v1`，标量动态 frame 为 `dynamic-analysis-frame-v6`。标量只读 `output.value`；每点/窗必须显式提供状态、质量和实际范围，不从旧 warmup 字段推导。发布、读取、预览、导出和 provenance 都校验当前格式；不合法结果统一拒绝，不添加旧格式转换或专属 UI。RBP 的 `band_values` 是当前五频带输出。

## Artifact 布局

数组不写入 SQLite JSON。当前布局：

```text
storage/artifacts/<run-id>/<artifact-id>.npz
```

写入过程在目标目录先生成临时文件，flush/fsync 后原子替换。SQLite 索引保存：kind、相对路径、media type、字节数、SHA-256、单位、每个数组 shape 和时间。读取前必须确认路径仍位于 artifact 根目录并验证 SHA-256。

相同缓存身份命中时，新 Run 保留自己的 ID，并用 `reused_from_run_id` 指向原始 Run；artifact 列表解析到原 Run 的不可变文件。

## 质量门

共享 Welch 频谱窗口默认质量原因如下；它们不是所有算法的统一质量阈值，各模块仍拥有自己的规则和参数快照：

| Code | 条件 |
| --- | --- |
| `non_finite` | 包含 NaN 或 Infinity |
| `amplitude_threshold` | 任一绝对幅值超过 150 uV |
| `flatline` | 任一通道峰峰值小于 0.5 uV |
| `clipping` | 任一通道极值重复至少 3 点且达到窗口样本的 1% |
| `missing_samples` | 本应完整的窗口样本数不足 |

Welch 只平均 clean segments，默认 clean ratio 小于 75% 时失败；最终以执行快照为准。不可用数值为 `null`，质量原因保留在 result summary 和 structured error 中；不得用 0 代替不可用结果。Partial 表示策略允许的不足整窗输入，不等于质量通过；算法仍独立执行质量门。

## 工程验证 API

```http
POST /api/validations
Content-Type: application/json

{
  "kind": "spectrogram_static_psd_parity",
  "algorithm_version": "offline-spectral-v3",
  "dataset_identity": {"kind": "synthetic"},
  "config_sha256": "<sha256>",
  "tolerances": {"rtol": 1e-7, "atol": 1e-9},
  "expected": [1.0, 2.0],
  "actual": [1.0, 2.0000000001]
}
```

读取：

```text
GET /api/validations
GET /api/validations/{validation_id}
GET /api/validations/{validation_id}/report
```

报告版本为 `engineering-validation-report-v1`，包含容差、最大绝对/相对误差、逐点通过率、环境和输入身份。PASS 只表示声明容差下的工程数值一致，不表示临床有效性。

## 结构化错误

新 API 的稳定错误码包括：

| HTTP | Code | 含义 |
| --- | --- | --- |
| 404 | `RECORDING_NOT_FOUND` | Recording 不存在 |
| 404 | `RUN_NOT_FOUND` | Run 不存在 |
| 404 | `VALIDATION_NOT_FOUND` | ValidationRun 不存在 |
| 409 | `RUN_NOT_CANCELLABLE` | 当前状态不能取消 |
| 409 | `ALGORITHM_RESULT_INVALID` | 已完成官方结果不满足当前契约 |
| 422 | `RUN_REQUEST_INVALID` | Run 参数或源身份无效 |
| 422 | `VALIDATION_REQUEST_INVALID` | 校验数组或容差无效 |

错误响应包含 `code`、`message`、`request_id` 和 `detail`。通用 `ApiError.detail` 仍是全站错误封装中的兼容字段，本轮未删除；它不解释旧算法结果。客户端按 `code` 判断，不解析消息文本。本轮 latest-only 清理限定官方结果链路，不声称已删除全项目所有兼容代码。

## 回滚与恢复

- schema migration 失败：应用启动失败，当前 migration 事务回滚，版本号不推进；
- artifact 元数据写入失败：对应临时/最终文件删除，不留下无索引结果；
- 代码恢复：本轮没有改写既有 Run/artifact，支持向前修复；不保证旧客户端或旧服务能读取当前结果契约；
- artifact SHA-256 不匹配：报告完整性错误，不返回为有效结果；
- 不允许通过删除或覆盖源 EEG 来“回滚”派生结果。
