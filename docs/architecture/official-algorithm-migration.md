# 官方算法版本与执行边界

更新日期：2026-10-22。本文件保留目录元数据引用的稳定路径，内容为当前边界；早期 shadow 迁移草案已由本说明替换。

官方算法通过独立模块和共享 Runtime 执行，不通过 Definition 图执行器。九算法由 `backend/app/algorithms/builtin_modules.py` 组合，manifest 负责科学版本、实现身份与显式 current；当前每个官方 ID 只允许一个 current。

版本升级后不执行或解释退休科学版本，不添加历史结果 reader、转换器、双写或专属 UI。保存的原始数据、Run/artifact 字节不因此删除；保留字节不意味着当前应用能解释退休格式。

Definition 版本和 digest 是不可变存储元数据，与科学版本、实现身份、结果格式分别管理。不能通过版本字符串排序或读取旧 Definition graph 来选择当前算法。

历史 shadow 模块、实时 Brainbeat EMA、旧多通道指标组合及其比较 CLI 已退休，不能按历史报告中的命令运行。

当前接入步骤见[作者指南](../algorithms/official-algorithm-development.md)，能力见[管理基线](../algorithms/official-algorithm-management.md)，数值与验收范围见[验证索引](../validation/README.md)。新的公式变更需独立冻结科学契约与验证，不能混入文档整理。
