# 算法与数据处理文档

本文档组描述当前实现。先按问题选择入口，避免将历史验证报告当成当前参数说明。

| 问题 | 阅读入口 |
| --- | --- |
| 新增算法、维护与排查 | [官方算法接入指南](official-algorithm-development.md) |
| 谁管理什么、三种策略、复用能力 | [管理基线](official-algorithm-management.md) |
| 九算法模块、输入和输出归属 | [执行清单](official-algorithm-execution-inventory.md) |
| 按需能力及验证入口 | [按需能力矩阵](on-demand-dynamic-capability-matrix.md) |
| 原始数据到算法 | [数据链路](01-data-pipeline.md) |
| 显示滤波与分析滤波 | [显示滤波](02-display-filter.md) |
| 导联配置与分析导联联动 | [导联](03-montage.md) |
| 频谱及各指标 | [频谱与离线指标](04-spectral-metrics.md) |
| 采集、回放与离线分析边界 | [实时与离线](05-realtime-vs-offline.md) |
| 独立参考、回归与证据 | [验证](06-validation.md) |
| 后端/WPF 代码路径 | [实现地图](07-implementation-map.md) |
| 时频轴、矩阵及功率 | [STFT](08-spectrogram.md) |
| 静态、动态与窗口时间 | [运行方式](09-analysis-modes-and-time.md) |
| 波形、游标与渲染 | [播放渲染](10-playback-rendering.md) |
| API、版本和单位 | [接口边界](11-api-contracts-and-units.md) |

公共行为以当前 OpenSpec 为准；人工说明发现冲突时应修订或记录差异。原始记录不可变，官方算法仅维护当前版本。数值验证和 GUI 验收分别记录，工程通过不代表临床有效性。
