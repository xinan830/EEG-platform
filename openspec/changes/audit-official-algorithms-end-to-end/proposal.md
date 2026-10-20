## Why

九个离线官方算法已有模块化实现和不同程度的自动化验证，但现有证据分散，工程正确性、独立数值复现和临床有效性容易被混为一谈。需要按统一审计方法逐项核验实现、数据契约、质量门和可追溯证据，明确哪些结论成立、哪些仍未验证。

## What Changes

- 建立 PSD、STFT、Peak Frequency、IAPF、Band Ratio、Theta/Beta、RBP、FAA、离线 Brainbeat 的版本化审计基线与证据矩阵。
- 逐算法审阅公式实现、通道和时间坐标、单位、窗口、滤波、质量门、失败语义及 provenance。
- 根据缺口补充边界回归和从原始输入开始的独立数值参考验证；真实记录验证仅使用经授权、去标识化的数据。
- 按证据分别评定工程实现、独立复算、边界覆盖和来源追溯，并形成问题清单与建议优先级。
- 明确离线 Brainbeat 与状态性实时 BrainBeat 是不同链路；工程或数值通过不宣称临床有效。
- 不修改算法公式、用户可见产品行为或公共 API；如审计发现需要改变科学契约，另立 OpenSpec change。

## Capabilities

### New Capabilities

无。本 change 记录一次性审计工作，不引入新的产品能力。

### Modified Capabilities

无。现有产品行为和科学契约不变；按 spec-driven 规则通过 `skip_specs: true` 声明无需 delta spec。

## Impact

- 审阅范围：`backend/app/algorithms/`、`backend/app/scientific/`、九算法及 runtime 测试、现有 OpenSpec 科学契约、验证运行及报告路径。
- 预期产物：逐算法证据矩阵、必要的测试和独立参考脚本/报告、经授权真实数据验证记录、最终评级与后续问题 backlog。
- 暂不改变 API、运行时算法、算法版本或桌面展示；若需代码修复或科学定义变更，先单独形成可审查变更。
- 兼容策略：只读历史 Runs 和 artifacts，不迁移、不重写、不删除历史结果。
