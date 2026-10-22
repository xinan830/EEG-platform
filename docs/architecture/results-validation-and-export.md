# 结果、验证与导出

WPF 算法工作区消费后端 Run、状态、质量、artifact 及 provenance，不计算 EEG 数值或推断临床结论。具体视图和结果处理器归各算法模块的桌面适配。

`GET /api/runs/{run_id}/result` 返回结构化结果视图；官方已完成结果先通过当前格式校验。非法格式返回 `ALGORITHM_RESULT_INVALID`，不转换退休结果。

`GET /api/runs/{run_id}/export` 产生 ZIP，包含 manifest.json、result.json、summary.csv、验证后的 NPZ artifact，以及按请求附带的持久化工程校验报告。包中不包含原始 EEG 样本、原始文件名或受试者身份；artifact 损坏或动态全量 coverage 不满足时失败，不能导出貌似完整的结果。

导出包是用户生成的派生结果，不应作为普通构建产物提交到 Git。仓库只保留明确受控、去标识的验证夹具。

ValidationRun 的 PASS 表示在指定输入、参考身份和容差下数值一致，不表示临床有效性。当前根结果格式、单位及错误边界见[接口说明](../algorithms/11-api-contracts-and-units.md)，可重跑证据见[验证索引](../validation/README.md)。
