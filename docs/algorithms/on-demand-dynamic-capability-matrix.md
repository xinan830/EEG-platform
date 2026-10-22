# 按需动态能力与验证入口

更新日期：2026-10-22。本文提供单窗边界和证据导航；完整三策略/双方向矩阵统一维护在[官方算法管理基线](official-algorithm-management.md)，机读声明见[策略矩阵](../validation/official-algorithm-strategy-matrix.json)。

九算法都有完整动态 Run 与按需单窗能力，WPF 可选择全量、按需或组合。模块声明窗口能力，公共服务协调；不能从 supported_modes 自动推断单窗实现或复用能力。

| 模块 | 单窗输入及专属规则 | Full→preview | On-demand→full |
| --- | --- | --- | --- |
| PSD | Welch、连续预处理、明确样本窗与谱质量门 | 支持 | 支持 |
| STFT | 独立时频轴/内部行、连续预处理、拒绝伪造行 | 支持 | 支持 |
| RBP | 固定预处理、五频带及分母 | 支持 | 不支持 |
| Band Ratio | 分子/分母功率、正分母门 | 支持 | 不支持 |
| Peak Frequency | 声明频段与频率网格 | 支持 | 不支持 |
| IAPF | 峰/COG、残差与失败证据 | 支持 | 不支持 |
| Theta/Beta | IAPF 驱动频段及功率 | 支持 | 不支持 |
| FAA | F3/F4 paired-epoch 与连续处理范围 | 不支持 | 不支持 |
| Brainbeat | Fz/Pz、独立逐窗 IAPF，无跨窗 EMA | 不支持 | 不支持 |

上表支持状态均限当前声明的工程验证路径；不能据此宣称所有真实记录和配置均验证过。最小窗口、Partial、尾窗、角色与滤波以各模块动态策略和 config 为准。

## 当前证据

- [组合策略专项报告](../validation/2026-10-21-official-combined-strategies.md)：九算法队列/窗口服务的合成对照、范围、状态、质量、provenance 和取消隔离。
- `backend/tests/test_official_directional_reuse.py`：PSD/STFT 逆向复用与损坏证据重算。
- [当前结果格式验证](../validation/current-algorithm-result-contract-validation.md)：完整结果与窗口消费者。
- [2026-10-15 人工验收](../validation/2026-10-15-offline-modes-wpf-acceptance.md)：仅代表该日期记录的 GUI 范围。
- [验证索引](../validation/README.md)：各阶段报告、独立参考及真实记录证据。

新增算法需要逐路径声明 implemented/validated/unsupported，并提供自己的证据。共享服务存在不自动使新算法的组合模式或复用变为 validated。
