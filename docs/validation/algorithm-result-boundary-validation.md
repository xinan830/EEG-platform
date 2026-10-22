# 算法结果边界整理验证（第 3 章）

日期：2026-10-22。变更：`clarify-algorithm-execution-boundaries`。本报告是工程回归证据，不是临床验证。

## 范围与实现

九个官方算法保持各自科学计算实现，使用现有四种结果类型；没有增加第五种结果类型。各 manifest 声明输出字段、单位、形状、角色顺序、质量/失败与证据能力。Runtime 在持久化前统一校验；公共序列化器负责四种结果类型和数组元数据，不按具体算法 ID 解释科学意义。FAA 的配对 FFT provenance 投影由 FAA 模块负责。

不合契约的模块结果以 `ALGORITHM_RESULT_INVALID` 保存为失败 Run；同步与队列执行均有测试，并确认不生成结果 artifact。

## 基线来源与比较规则

- `algorithm-result-boundary-baseline.json/.npz`：九算法 × 静态/动态 × 正常/高幅值输入，共 36 个场景。输入为确定性合成信号，200 Hz、30 秒，五通道；记录输入样本 checksum、单位和通道顺序。高幅值场景将输入乘以 30，具体质量判断仍由各算法负责。
- 该基线在结果序列化器提取之前捕获；FAA 公共 provenance 预期由提取前的 HEAD 源码生成。基线保留第 2 章已增加的 typed 输入证据，因此不宣称它是第 2 章之前的完整 provenance 基线。
- 原有 `official-algorithm-strategy-baseline.npz` 未重生成。九算法动态正常场景的 21 个数值数组另外与它交叉比较，防止新增基线自证。
- Run summary、输入身份、配置、实际范围、状态、质量、失败和 provenance 按结构逐字段比较。离散字段、坐标和单位精确一致；科学数值和中间量使用 `rtol=1e-10`、`atol=1e-20`。频率/时间轴逐点精确一致。
- 排除每次测试新分配的 Definition 行 ID、含该分配身份的缓存键、artifact UUID/路径/压缩文件 checksum，以及 `raw-recording:<uuid>` 导联 ID 中的分配 UUID。后者仍验证身份格式，科学 fingerprint 不豁免比较。artifact 的 kind、unit、shape、media type 保留比较，每次新 artifact 都验证自身 SHA-256。
- 受控 NPZ SHA-256：`66e020b9656f7613f30824d126a6cc96372728335f1100f5e3ad831f3eeb7717`，大小 525,506 字节；NumPy 2.3.4、SciPy 1.16.3。仅保存合成输入产生的结果，无真实 EEG 或身份信息。

## 实测结果

36 个基线场景全部通过。58 次数组比较覆盖 172,448 个数值点；最大绝对误差、最大相对误差均为 **0**，失败坐标为空。摘要比较覆盖 5,910 个科学数值字段；最大绝对/相对误差均为 **0**，失败位置为空。原有 21 个动态数值数组也通过原冻结容差。

测试在 JUnit 的 `array_comparison`、`summary_comparison` 属性中输出每个场景的最大误差、比较数量和失败位置。临时 JUnit 位于忽略目录 `.artifacts/chapter3-regression.xml`，受控冻结件与本报告作为可重放证据保留。

回归入口为 `test_result_boundary_baseline.py`、`test_algorithm_result_boundary.py`，并联同官方 Run、provenance、runtime、combined、双向复用、策略矩阵、独立数值参考、注册表、目录 API 和 Run queue 测试执行，最终 **173 项全部通过**。新增算法 ID 使用相同验证/序列化路径的测试通过。OpenSpec strict、文件规模检查、`git diff --check` 均通过。

## 覆盖边界

本报告未覆盖每种导联 × 滤波 × 缺失数据 × 时间范围的笛卡尔积，也不替代桌面人工验收。combined 与双向复用沿用专项回归；36 场景冻结件覆盖静态/动态全量结果，不宣称新增了所有策略组合的独立冻结快照。

第 3 章记录的是整理当时的行为：动态标量状态归一化及 `warmup` 重复输出暂时保留，四个 producer 依赖运行时生成 states。这些行为现已在第 4 章删除，producer 显式提供状态，后端与 WPF 使用当前契约。原冻结件保持不变，测试仅投影已批准的 wire 字段变化后比较科学数值与证据。最新证据见 [第 4 章验证报告](current-algorithm-result-contract-validation.md)；本章报告不再作为当前 wire 格式说明。
