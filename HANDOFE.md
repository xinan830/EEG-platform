# 项目交接说明

更新日期：2026-10-22。本文说明当前实现；旧阶段计划和历史结论不作为当前待办。

## 当前基线

算法执行边界整理与官方导联预设修复已由用户验收，提交为 `861ba46`。对应变更 `clarify-algorithm-execution-boundaries` 为 22/22，主规范已同步，变更记录尚未归档。该提交的完整后端 1,049 项、WPF 917 项测试通过；这是该提交的验收证据，不是后续任意版本的自动保证。详见[验收报告](docs/validation/algorithm-boundary-cleanup-acceptance.md)。

本轮文档整理使用 `refresh-current-project-documentation`，其进度以 [tasks](openspec/changes/refresh-current-project-documentation/tasks.md) 为准。

## 架构与责任

- WPF/.NET 10：设备集成、采集、原始存储、阅图、参数与结果展示。
- FastAPI/Python：记录读取适配、导联投影、分析预处理、算法执行与验证、Run/artifact 持久化。
- 九个算法包位于 `backend/app/algorithms/`，由 `builtin_modules.py` 唯一组合。公式和算法专属质量门属于算法包。
- `algorithm_runtime/` 定义输入/输出和校验；`services/` 协调队列、窗口、缓存、取消与 provenance。WPF 有独立 UI 和结果 handler 注册点。
- 当前 Definition 图仅作元数据与拓扑校验，不执行自定义公式或图。扩展 manifest 校验不等于插件安装或执行。

## 数据与导联

内部时序为 V/float64、`(samples, channels)`；通道顺序、采样率、录制相对绝对样本范围和单位必须显式保存。录制时间、游标、分析窗口与显示历史不能混用。

算法详情先选择“导联配置”，再按算法角色选择“分析导联”。候选来自采集快照、基于记录通道快照生成的官方预设，以及兼容的当前用户配置；后端校验并固化独立分析快照。切换后更新输入身份、选项、计算和追溯。源通道不足不能补造。阅图显示导联与算法分析选择分开管理，原始样本不修改。

滤波和频率输出范围分别声明；不能把截取 1–30 Hz 频谱当成已经预滤波。统一输入契约不要求 FAA、STFT、Brainbeat 等使用相同科学计算。

## 版本与验证边界

官方算法只解析显式 current。完整结果根格式为 `official-algorithm-result-v1`，标量动态格式为 `dynamic-analysis-frame-v6`，状态显式返回。不提供退休算法/结果 reader、转换器或专属 UI；已保存 Run/artifact 字节不迁移、不删除。

不能据此称全项目“零兼容”：数据库结构迁移、通用 API 错误封装及少量 WPF 命名空间适配是不同边界。

三种运行策略和方向性复用的工程证据见[管理基线](docs/algorithms/official-algorithm-management.md)。真实长记录、合成独立参考、GUI 验收和临床验证不可相互替代。历史报告保留原日期与范围，统一从[验证索引](docs/validation/README.md)阅读。

## 维护入口

1. 修改前读 [工程开发契约](docs/工程开发契约.md)和相关当前 OpenSpec。
2. 新增算法按[作者指南](docs/algorithms/official-algorithm-development.md)接入，不在公共层复制公式。
3. 排查先固定 request_id、Run/window、导联快照、通道顺序、样本范围和处理身份，再查模块数值，最后查 WPF 投影。
4. tasks 随工作更新；验证命令、结果及限制写入对应 change。
5. 提交前核对 diff、规模策略与相关测试；不纳入 `.artifacts`、bin/obj、DLL/PDB、日志、ZIP、数据库或真实 EEG。

文档过时应更新或删除；旧草案从 Git 恢复，不另建平行计划树。带日期的证据不自动升级为当前行为说明。
