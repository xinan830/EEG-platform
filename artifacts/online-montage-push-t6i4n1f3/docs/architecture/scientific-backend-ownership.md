# 科学后端实现归属

更新日期：2026-10-22。路径相对仓库根目录；本表解释当前所有权，公共行为仍以当前规范为准。

| 责任 | 当前实现 |
| --- | --- |
| 共享科学契约 | `backend/app/scientific/contracts/` |
| 共享预处理、Welch、band power 和 spectrogram | `backend/app/scientific/primitives/spectral.py` |
| 共享谱质量检查 | `backend/app/scientific/quality/` |
| PSD/STFT/RBP 与六个其他指标的公式和专属规则 | `backend/app/algorithms/<id>/` |
| 官方模块唯一组合 | `backend/app/algorithms/builtin_modules.py` |
| 目录、Definition 元数据组合 | `backend/app/algorithms/catalog.py` |
| 通用执行与 typed 契约校验 | `backend/app/algorithm_runtime/` |
| 记录到算法输入的适配 | `backend/app/services/algorithm_recording_context.py` |
| 公共谱读取与连续预处理编排 | `backend/app/services/spectral_analysis.py` |
| Definition 拓扑/参数校验 | `backend/app/definitions/validation.py` |
| 闭集类型和节点词汇 | `backend/app/eeg_core/primitives/` |

各算法的准确输入与输出见[执行清单](../algorithms/official-algorithm-execution-inventory.md)。Runtime、Run 服务和 WPF 不拥有算法公式。

## 参考与非执行元数据

`backend/app/services/independent_spectral_reference.py` 是独立参考验证端点的实现，不是生产 Runtime 的注册算法；端点适用范围见[参考校验说明](independent-spectral-reference-validation.md)。其余独立参考与冻结件位于测试和验证目录。

Definition 校验和扩展 manifest 校验不执行图、公式或任意 Python。闭集原语存在不代表用户已获得自定义执行能力。

## 版本边界

已退休的实时指标组合、shadow CLI、graph/formula 执行器和官方科学转发 facade 不再是可调用入口。当前官方算法及结果只维护最新声明契约；存储结构迁移、API 封装和 WPF 命名空间适配另行管理。

新增科学逻辑放入当前所有者，不能为了旧测试保留转发别名；原始 EEG、Run 和 artifact 不因结构整理而迁移或删除。
