## 1. 基线与依赖确认

- [x] 1.1 验证上一统一管理变更，保留简明验证报告并归档，同步当前规格，消除旧 compatibility imports 要求与 latest-only 要求的冲突。
- [x] 1.2 记录退役模块、别名、重导出、工具、测试、authority manifest 和文档的消费者清单；区分当前 Definition 校验与历史执行职责。
- [x] 1.3 记录当前九算法独立参考身份、数值/策略基线 checksum 和三种策略验收路径，作为删除后的比较基准。

## 2. 回归约束

- [x] 2.1 为 WPF 两条 Full Run 预览路径补充缺失 STFT provenance 返回结构化不可用的测试，以及当前完整 provenance 正常显示的测试。
- [x] 2.2 为当前 Definition 图/参数校验保留未知节点、循环、缺失依赖及参数错误的稳定错误码契约测试。
- [x] 2.3 增加架构约束，禁止生产代码及当前验证工具重新导入退役计算器或历史转发入口。

## 3. 后端退役清理

- [x] 3.1 删除 legacy shadow 实现及专属包装、旧实时 spectral helpers 和纯历史测试；将混合测试中当前职责的断言迁入当前测试。
- [x] 3.2 将 Definition 图/参数校验迁入当前领域归属，更新服务引用，删除无生产消费者的 graph/formula 执行器及旧目录入口，不保留 shim。
- [x] 3.3 清理无消费者的旧 scientific descriptor、常量别名、工具和 authority manifest 项；保持当前算法数值与独立参考不变。

## 4. WPF 与当前文档

- [x] 4.1 核对后端窗口 provenance 到 WPF DTO 的传输，为两条 Full Run 预览路径传递真实证据；删除历史 STFT provenance 推断和 compatibility 标记。
- [x] 4.2 缺少当前契约时呈现稳定错误码、不可用状态和重新计算说明，验证不会显示伪造数值或触发未处理 UI 异常。
- [x] 4.3 删除旧实时指标当前文档及索引，修订关联当前说明、实现地图和所有权文档；保留明确标注的历史审计记录。

## 5. 验收与交付

- [x] 5.1 运行后端全套测试及九算法数值/策略基线比较，证明 full/on-demand/combined、Definition 校验和数据库结构迁移未回归；记录差异与错误坐标。
- [x] 5.2 运行 WPF 测试和构建，核对两条预览路径的正常与不可用呈现、时间范围、单位、状态和 provenance。
- [x] 5.3 运行 OpenSpec strict、文件规模检查、退役引用扫描和 git diff --check；记录报告并逐项同步 tasks，只提交源码、测试、文档及受控验证文件。
