# 闭集原语与 Definition 校验边界

`backend/app/eeg_core/primitives/` 保存 typed 数据类型、节点注册表和若干纯计算组合。`backend/app/definitions/validation.py` 使用闭集词汇校验 Definition 拓扑、参数及类型；不会因此执行 EEG 图或公式。

这些原语不是官方算法的另一套注册入口。当前官方科学实现归 `backend/app/algorithms/` 和 `backend/app/scientific/`，共享执行归 `algorithm_runtime/`。例如原语组合 `fixed_band_rbp` 或 `frontal_alpha_asymmetry` 的存在，不等于当前官方 RBP/FAA 使用该组合或其全部质量规则。

原语的数据类型显式携带单位、轴、通道与 provenance，未知节点或不匹配单位应拒绝。不可变数组和严格单位类型不能替代独立数值验证，也不能推出动态策略、GUI 或插件能力已完成。

当前用户 Definition 不提供图执行、scalar formula preview、eval、动态导入或任意 Python。扩展接口仅校验 manifest 元数据，见[扩展边界](extension-governance.md)。新增官方算法走[作者流程](../algorithms/official-algorithm-development.md)，不重新接通退休解释器。
