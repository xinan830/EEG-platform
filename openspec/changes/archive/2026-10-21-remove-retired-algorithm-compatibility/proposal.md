## Why

官方算法已统一为 current scientific version，但旧 shadow 参考、旧实时辅助模块和 WPF 历史 artifact provenance 补齐仍留在仓库，增加误用和维护成本。需要删除这些退役路径，使实际依赖和 latest-only 管理政策一致。

## What Changes

- 删除仅用于历史迁移比较的 shadow 实现、旧实时指标辅助实现及专属工具、测试和当前文档引用；当前独立数值参考与冻结基线必须保留。
- **BREAKING**：删除 WPF 为缺少当前 provenance 的旧全量 artifact 推断科学语义的行为；显示结构化不可用原因，要求重新计算。
- 盘点 `app/legacy/definition_engine.py`：保留当前 Definition 服务需要的图/参数校验，将其移到当前归属；删除无生产消费者的执行与公式入口，不保留转发兼容层。
- 清理仅服务于退役版本的常量别名和 facade，并增加禁止重新引入的架构检查。
- 更新当前文档中的旧指标维护说明与链接；历史决策记录保留为历史证据。

## Capabilities

### New Capabilities

- `analysis/retired-algorithm-removal`: 定义退役实现删除、当前职责归属及旧 artifact 不可用行为。

### Modified Capabilities

无。已有统一管理变更继续规定官方算法 latest-only 身份；本变更补充清理验收边界，不重复维护其契约。

## Impact

涉及 backend 的历史参考、Definition 校验依赖、相关工具与测试，WPF 的 Full Run 窗口显示适配及文档。当前九算法公式、单位、通道顺序、质量规则、full/on-demand/combined 策略和数值基线不变。

不删除原始 EEG、Run、artifact 或数据库 schema migration；不扩大到采集、导联、显示校准及缓存故障保护。旧 artifact 不迁移、不重解释；缺少当前契约的结果需重新运行。部署前记录现有数据与提交身份，回滚通过恢复软件提交完成，不改写存储结果。测试为工程验证，不代表临床验证。
