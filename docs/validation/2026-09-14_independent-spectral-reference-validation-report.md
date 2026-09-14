# 独立频谱参考校验验证报告

日期：2026-09-14

## 范围

本次变更为 `offline-spectral-v3` 增加独立 SciPy/MNE PSD 参考校验与可导出逐点证据。它没有修改生产频谱数学、Viewer Pipeline 或既有频谱 API。

## 证据

| 检查 | 结果 |
| --- | --- |
| 合成 10 Hz/20 Hz 混合信号逐点 PSD 比对 | PASS，234 个点，2 个通道 x 117 个频点 |
| 非文件顺序通道请求 | PASS，`F3, Fz` 原样保留 |
| 独立路径不调用生产 PSD helper | PASS，生产 helper 被替换为抛错函数后独立计算仍完成 |
| 质量门失败 | PASS，返回 `SPECTRAL_REFERENCE_UNAVAILABLE`，未写入零值证据 |
| 报告和结果导出 | PASS，包含 derived PSD evidence，不包含原始 EEG 样本或文件名 |
| SQLite 旧库升级与重复迁移 | PASS，`evidence_json` 非破坏式加入 `validation_runs` |
| 后端全量测试 | PASS，162 passed |
| 前端类型检查、Vitest、生产构建 | PASS |
| OpenSpec strict validation | PASS |

## 结论

在声明的工程容差内，生产 `offline-spectral-v3` PSD 可被独立 NumPy/SciPy 参考实现逐点复核。该结论仅说明实现一致性，不表示算法具有临床有效性。
