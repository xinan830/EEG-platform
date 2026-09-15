# 可追溯分析底座

> 当前边界：本地单用户工作台。标准 `/api/runs` 使用 SQLite 持久队列和单 Worker；旧 `/api/recordings/{id}/analysis` 为兼容旧客户端而同步返回，但其新结果同样由标准 Run/Artifact 链保存。

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

旧 `/api/recordings/{id}/analysis`、`/spectrum/configured` 和 `/spectrogram/configured` 继续可用。旧 analysis 路由的新请求不再写入历史 `analyses` 表，而是同步执行并返回一个 `legacy_analysis` AnalysisRun；`GET /api/analyses/{id}` 仍可读取迁移前的历史 JSON 记录。新功能不删除旧结果，也不把 Viewer 的 montage/filter 隐式带入 Analysis。

## 数据库升级

当前 schema version 为 `7`：

| Version | 内容 |
| --- | --- |
| 1 | 收拢现有 recordings、analyses、audit、events、reports 表 |
| 2 | Recording source 和通道导入身份 |
| 3 | AnalysisRun、artifact、ValidationRun |
| 4 | 不可变算法定义和版本 |
| 5 | Project、Subject、Session、Condition |
| 6 | 持久 Run 队列和 BatchRun |
| 7 | Validation evidence |

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

创建一个 10 秒静态 PSD Run：

```http
POST /api/runs
Content-Type: application/json

{
  "recording_id": "<recording-id>",
  "analysis_type": "spectrum",
  "config": {
    "mode": "static",
    "channels": ["Oz", "Fz", "Pz"],
    "time": {"start_s": 10.0, "end_s": 20.0}
  }
}
```

当前接口同步执行并返回 HTTP 201。状态仍完整记录为：

```text
queued -> running -> completed | gate_failed | failed
queued -> cancelled
```

查询资源：

```text
GET  /api/runs?recording_id=<id>&limit=100
GET  /api/runs/{run_id}
POST /api/runs/{run_id}/cancel
GET  /api/runs/{run_id}/artifacts
```

当前同步执行进入 `running` 后不能被协作式中断，因此终态或运行态取消返回 HTTP 409、`RUN_NOT_CANCELLABLE`。Change 06 引入持久 worker 后会另行修改创建和取消语义。

## 缓存身份

规范 JSON 使用 UTF-8、对象键排序、紧凑分隔符、保留数组顺序、拒绝 NaN/Infinity。缓存 SHA-256 的输入固定为：

```text
analysis-cache-v1
+ source file SHA-256
+ algorithm definition SHA-256
+ resolved configuration SHA-256
+ implementation build identity
+ actual absolute time range
```

即使科学定义版本不变，只要 PATCH 修复可能改变数值，也必须改变 `BRAIN_PLATFORM_BUILD_VERSION` 或包构建版本，从而生成新的缓存身份。

## Artifact 布局

数组不写入 SQLite JSON。当前布局：

```text
storage/artifacts/<run-id>/<artifact-id>.npz
```

写入过程在目标目录先生成临时文件，flush/fsync 后原子替换。SQLite 索引保存：kind、相对路径、media type、字节数、SHA-256、单位、每个数组 shape 和时间。读取前必须确认路径仍位于 artifact 根目录并验证 SHA-256。

相同缓存身份命中时，新 Run 保留自己的 ID，并用 `reused_from_run_id` 指向原始 Run；artifact 列表解析到原 Run 的不可变文件。

## 质量门

共享频谱窗口质量原因：

| Code | 条件 |
| --- | --- |
| `non_finite` | 包含 NaN 或 Infinity |
| `amplitude_threshold` | 任一绝对幅值超过 150 uV |
| `flatline` | 任一通道峰峰值小于 0.5 uV |
| `clipping` | 任一通道极值重复至少 3 点且达到窗口样本的 1% |
| `missing_samples` | 本应完整的窗口样本数不足 |

Welch 仍只平均 clean segments，并在 clean ratio 小于 75% 时失败。`gate_failed` Run 的 PSD、Band Power、RBP 为 `null`，质量原因保留在 result summary 和 structured error 中；不得用 0 代替不可用结果。

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
| 422 | `RUN_REQUEST_INVALID` | Run 参数或源身份无效 |
| 422 | `VALIDATION_REQUEST_INVALID` | 校验数组或容差无效 |

错误响应仍包含 `code`、`message`、`request_id` 和旧客户端兼容字段 `detail`。

## 回滚与恢复

- schema migration 失败：应用启动失败，当前 migration 事务回滚，版本号不推进；
- artifact 元数据写入失败：对应临时/最终文件删除，不留下无索引结果；
- 代码回滚：新增表和列可以保留，旧服务继续读取旧字段；
- artifact SHA-256 不匹配：报告完整性错误，不返回为有效结果；
- 不允许通过删除或覆盖源 EEG 来“回滚”派生结果。
