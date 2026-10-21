# 官方算法管理基线

本文记录官方算法在后端、桌面端和验证材料中的字段归属及接入基线。算法公式、预处理和质量规则仍由各算法包负责；共享管理层协调身份、运行策略、调度、持久化和 provenance。

## 字段归属和消费者

| 字段/职责 | 权威维护位置 | 主要消费者 | 当前边界 |
| --- | --- | --- | --- |
| 算法 ID、中文名称、缩写、用途、科学版本、实现身份、current 标记、支持模式、输出声明、策略能力 | `backend/app/algorithms/<id>/manifest.py`；模块组合在 `builtin_modules.py` | `algorithm_runtime`、`algorithms/catalog.py`、API、Run、动态结果校验 | 科学版本与实现身份、Definition 版本、动态结果契约版本语义不同，不能合并 |
| Definition 名称/版本、可选不可变描述 payload | manifest 的 `definition_persistence` | Definition repository、Run/window identity provenance | 属存储 provenance；已发布记录不可变。旧描述 payload 可能与当前科学版本不同，不提供旧计算器执行路径 |
| 参数模型、通道解析、执行快照、静态/动态计算 | 各算法包的 `config.py`、`runner.py`、`compute.py`、`window.py`、`official.py` | 后端 runtime/executor、Run 服务、按需 worker | 公式及算法专属质量/通道角色不得移入通用运行时或 WPF |
| 算法注册与执行生命周期 | `backend/app/algorithm_runtime/` 和闭合工厂表 `backend/app/algorithms/builtin_modules.py` | catalog、Run、动态窗口能力注册、worker | 共享层执行通用校验、解析、调度和 provenance；科学计算委托模块 |
| Definition 图与持久化摘要 | `backend/app/algorithms/catalog.py`，由模块元数据生成；校验在 `backend/app/definitions/validation.py` | Definition repository、当前存储和校验服务 | 图结构不自动等同于官方科学实现；不提供旧图执行器或公式预览；RBP 有额外 graph contract/digest 约束 |
| UI 标题、排序、详细视图工厂、通道提示和显示参数 | WPF `AlgorithmUiRegistry.cs` | 算法工作区、detail view model | presentation metadata；不拥有科学身份或公式 |
| Run 结果映射、结果投影和按需视图能力 | WPF `AlgorithmRunResultHandlerRegistry.cs` 及各算法 handler/detail | Run 展示、按需预览、全量结果投影 | 是独立客户端集成点；不能仅凭后端目录项推断已完成 |
| 三种策略与方向性复用调度 | 后端动态能力 registry、window reuse/publisher/consumer；WPF `AlgorithmListViewModel` 共享调度 | 动态 API、Run 和桌面交互 | full→preview 与 on-demand→full 是不同方向；声明、代码实现、验证证据分开记录 |
| 验证证据 | `backend/tests/`、`desktop-client/BrainPlatform.Desktop.Tests/`、`docs/validation/` | 发布检查和审阅 | GUI 正常路径、自动化等价测试、组合压力证据不是同一种证据 |

## 策略和复用状态矩阵

状态含义：`implemented` 表示代码路径存在；`validated` 表示有对应策略/方向的验证证据；`unsupported` 表示当前明确不提供；`unvalidated` 表示实现或可能性存在，但缺少该路径所需的证据。正常单独路径的历史验收不能推导组合压力验证。矩阵中的 combined 状态不把两种独立模式的成功相加。

| 算法 | Full | On-demand | Combined preview + background full | Full→preview | On-demand→full |
| --- | --- | --- | --- | --- | --- |
| PSD | implemented / validated | implemented / validated | implemented / unvalidated | implemented / validated | implemented / unvalidated |
| STFT | implemented / validated | implemented / validated | implemented / unvalidated | implemented / validated | implemented / unvalidated |
| RBP | implemented / validated | implemented / validated | implemented / unvalidated | implemented / validated | unsupported |
| Band Ratio | implemented / validated | implemented / validated | implemented / unvalidated | implemented / validated | unsupported |
| Peak Frequency | implemented / validated | implemented / validated | implemented / unvalidated | implemented / validated | unsupported |
| IAPF | implemented / validated | implemented / validated | implemented / unvalidated | implemented / validated | unsupported |
| Theta/Beta | implemented / validated | implemented / validated | implemented / unvalidated | implemented / validated | unsupported |
| FAA | implemented / validated | implemented / validated | implemented / unvalidated | unsupported | unsupported |
| Brainbeat | implemented / validated | implemented / validated | implemented / unvalidated | unsupported | unsupported |

矩阵依据是现存模块/handler 能力声明和历史验证材料，按当前 manifest 声明及其 evidence 文件核对；实现和验证仍是独立状态。Full→preview 的七项实现范围来自 `docs/algorithms/on-demand-dynamic-capability-matrix.md`；FAA、Brainbeat 未声明该方向。当前只有 PSD、STFT 模块实现 `can_reuse_offline_window`，其余算法声明 unsupported；共享发布/消费基础设施不能代替算法专属数值/形状判定。组合真实记录压力证据当前明确覆盖 PSD（`docs/validation/2026-10-17-background-full-orchestration-real-psd.md`）；2026-10-15 九算法验收证明正常用户路径，不等同于九算法各自的组合压力验证。矩阵需由后续自动 gate 和逐算法证据更新，不能手工把 `unvalidated` 改成 `validated`。

可机读、由回归测试核对的当前策略和契约矩阵见
[official-algorithm-strategy-matrix.json](../validation/official-algorithm-strategy-matrix.json)。它冻结九个模块的 manifest、参数 schema、输出 schema 指纹，并逐条标出验证测试；shared combined 生命周期测试只证明共享调度与前台/后台状态隔离，不证明每个科学算法的组合数值等价。

独立数值参考和当前合成输入/输出数组快照见
[official-algorithm-numeric-baseline.json](../validation/official-algorithm-numeric-baseline.json) 与
[NPZ 数组 artifact](../validation/official-algorithm-numeric-baseline-arrays.npz)。报告记录 NumPy/SciPy 版本及 artifact SHA-256；已有独立参考测试对 PSD/STFT 轴和矩阵以及标量、FAA、Brainbeat 指标做数值比较。当前冻结数组 artifact 仅包含合成输入及 PSD/STFT 输出，尚未冻结九算法每条已支持策略/复用路径的 expected/actual 全量数组、逐点最大误差和失败坐标；因此 OpenSpec 5.1 仍未完成。组合模式九算法专属状态、质量、范围和 provenance 的证据也未齐，4.4 仍未完成。以上是工程验证，不是临床验证。

## Latest-only 版本规则

每个官方算法 manifest 显式设置 `is_current_version=True`。官方解析必须走显式 current resolver，且每个官方 ID 恰好一个 current。版本字符串不参与“最新”排序。未标 current、没有 current 或有多个 current 都应失败。通用/用户算法的注册语义与官方策略分开；此字段不隐式赋予它们官方身份。

已退役科学版本不提供执行、迁移、解释或展示兼容；此策略不要求删除数据库中已有 Run、记录或 artifact 字节。Definition semver/digest 是单独的不可变存储 provenance，不等于科学版本；已部署 Definition 记录继续按其存储 digest 标识，当前通用官方结果适配器不会执行其历史 graph。版本推进时需要更新算法模块、身份、针对性验证证据和 current 声明，并运行 completeness gate。


## 新算法接入清单

1. **冻结科学契约**：在 OpenSpec 写明算法 ID、科学版本、输入/输出单位、频率轴或标量含义、通道角色、预处理、窗口/质量语义、provenance 和容差。参考 [官方算法接入约定](../../.agents/skills/brain-research-platform/references/official-algorithm-development.md)；提交前运行 `openspec validate <change> --strict`。
2. **实现算法包**：在 `backend/app/algorithms/<id>/` 增加 manifest、config、runner 和必要的 compute/window/evidence 文件；公式和算法专属质量逻辑留在该包。每个公式变更需添加独立数值参考或边界测试。
3. **声明身份与策略**：manifest 明确 scientific version/current、implementation identity、output schema、supported modes、capability implementation/validation/evidence；full→preview 与 on-demand→full 分方向申报，缺少专项证据时保留 `unvalidated`。
4. **注册后端一次**：在 `backend/app/algorithms/builtin_modules.py` 加入唯一工厂；参数 schema 和运行时配置由模块提供。运行 `python -m pytest backend/tests/test_builtin_algorithm_registry.py backend/tests/test_algorithms_catalog_api.py backend/tests/test_algorithm_runtime_architecture.py -q`，检查目录与 manifest parity、current 唯一性、科学实现边界和注册缺口。可运行 manifest 缺少 runtime module 或目录身份/输出契约不一致时，API 必须返回 `OFFICIAL_ALGORITHM_CATALOG_UNAVAILABLE`，不得拼出默认参数或动态策略。
5. **接入 WPF 两个进程内注册点**：在 `AlgorithmUiRegistry.cs` 注册 detail factory/presentation；在 `AlgorithmRunResultHandlerRegistry.cs` 注册结果 handler，并完成参数序列化、输出绑定、状态/null/failure/provenance 展示。不能仅凭后端 catalog 声称桌面支持完整。
6. **补契约与行为测试**：后端测试覆盖配置边界、科学输出、状态、范围与 identity；WPF 测试覆盖 catalog JSON 的 identity、参数、能力、输出 schema 解码，以及 handler、请求、显示、窗口投影及错误。运行 `dotnet test desktop-client/BrainPlatform.Desktop.Tests`。注册完整性由 `official_registration_report()` 检查 UI、handler、detail 和已声明的 on-demand/full→preview 支持。Catalog parity 由 `backend/tests/test_algorithms_catalog_api.py` 验证；WPF wire contract 由 `AlgorithmCatalogCapabilityTests` 验证。
7. **产生策略验证证据**：对声称 validated 的每条路径提供对应自动化测试或受控验证报告；combined 必须有组合执行专属证据。验证 Run identity 从请求冻结到结果、实际样本范围、source/serving provenance 和 artifact checksum。
8. **发布前闭环**：更新本文矩阵和 evidence 链接；运行后端全套、WPF 全套、`openspec validate <change> --strict`、`git diff --check`；检查变更文件规模符合 [`docs/工程开发契约.md`](../工程开发契约.md)。

官方算法采用 latest-only：版本升级后只解析显式 current；退役官方科学版本没有执行、迁移、解释或展示兼容路径。保留的历史 Run/artifact 字节属于审计数据，不代表其可由当前算法重新计算或由 UI 解释。


## Run provenance and latest-only retirement

For an official Run, the trace chain is frozen at enqueue time: algorithm ID, Definition ID/version, scientific version, module-owned implementation identity, implementation build, scientific configuration hash/cache key, selected execution mode, requested and actual sample bounds, result strategy provenance, and artifact checksum. `analysis_provenance` is a projection of persisted values and result evidence; it does not calculate metrics or fill missing historical identity values.

Retired versions remain identifiable when their stored metadata is present, so an audit can distinguish the old identity and artifact. The current application does not execute, migrate, interpret, or add special display compatibility for retired versions. New requests resolve only the explicitly marked current version and reject retired identities before EEG loading or queueing.


## 兼容策略清理

官方算法只维护当前 scientific version。旧 FAA evidence 不再被重新解释，缺少当前 FAA contract 的历史结果不会被投影为当前 provenance；缺少动态 coverage provenance 的旧全量 Run 不再导出。频谱服务使用当前 ScientificSpectralGateway，不保留历史 monkeypatch/facade 入口。数据库 schema migration 仍保留，因为它负责数据库启动和结构升级，不执行旧科学算法。

退役清理后，旧实时频谱、shadow 包与脚本、旧三通道指标组合、graph/formula 执行器和测试 provider 旧签名适配均已删除。当前 Definition 校验归入 `backend/app/definitions/validation.py`；Brainbeat 纯公式归入自己的算法包。WPF 两条完整结果预览路径只使用后端保存的 STFT 窗口证据，缺失或不符合当前契约时返回 `FULL_WINDOW_CONTRACT_UNAVAILABLE` 并要求重新计算，不补造 provenance。
