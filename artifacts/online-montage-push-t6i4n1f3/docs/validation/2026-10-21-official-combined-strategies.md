# 官方算法组合策略工程验证

日期：2026-10-21。起始提交：769514c。范围：合成输入下的工程一致性，不是临床验证或真实长记录压力验收。

## 结果与修复

九算法 combined 和 PSD/STFT on-demand→full 均有逐算法专项证据。未提供的复用方向继续为 unsupported。新算法不会自动继承本轮 validated 状态，必须在自己的 manifest 中显式登记证据，并进入参数化测试清单。

测试发现并修复 `OfflineWindowReuse` 的状态竞态：后台 provider 的窗口快照生成后，新预览可能已完成；旧逻辑对该终态窗口创建新 attempt，产生 `terminal dynamic window cannot receive a new attempt`，将本来有效的窗口变成不可用。当前逻辑读取持久化状态，已完成结果继续经过原身份、checksum、质量和算法专属形状校验；真正属于当前 full Run 的新 producer 才进入计算。未修改科学公式或放宽验证。

## 验证方法

- 真实 WPF 合成文件 reader、SQLite 队列、窗口服务和共享计算门控；数据在临时目录生成，200 Hz，30 s，内部 V/float64。按算法使用 F3/F4 或 Fz/Pz 顺序。
- 对照组通过当前 executor 先计算全量，不读取任何按需缓存。这是策略路径对照，不是独立公式参考；独立参考仍由原数值验证套件提供。
- 组合组在后台已完成第一个科学窗口、释放计算 lease 后同步请求预览，断言 Run 仍 running 且进度为 1。预览与对应对照窗口比较轴、数组、状态、质量、中间证据、实际范围和科学身份。
- 九算法分别验证取消预览订阅后全量继续完成，以及取消后台后已完成预览保持可用。质量拒绝输入保留结构化原因和空结果。
- 全量结果逐数组/逐窗比较对照；STFT 复用窗口保留实际 on-demand producer provenance，科学坐标语义与无缓存结果一致，不要求运输来源字段相同。
- PSD/STFT 单独验证真实复用计数为 1，并比较无缓存全量；缺少 manifest digest 或 cache key 不匹配时，复用计数为 0，明确记录 miss reason 后重算。
- WPF 九算法逐项验证预览/后台请求参数一致、科学通道角色正确、幂等身份和后台取消不覆盖预览；WPF 全套另含已完成全量投影及 failure 展示测试。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| 后端全套 | 870 passed，7 个既有警告，306.25 s |
| 最终策略、复用及矩阵专项 | 41 passed |
| WPF 全套及本轮编译 | 862 passed，0 failed，0 skipped |
| WPF 新增逐算法策略测试 | 9 passed |
| OpenSpec 正式规格 strict | 59 passed（含归档后的退役清理规格） |
| 本轮 change strict、源码规模、diff --check | passed |

后端全套运行时已包含生产修复与能力声明。其后新增质量拒绝参数项、完善诊断属性，并将组合同步点加强为第一个真实科学窗口后；最终 41 项专项验证覆盖这些测试改动。WPF 存在既有 NuGet 框架兼容及 xUnit analyzer 警告；后端警告来自既有 FastAPI/Starlette 弃用提示和非有限数值边界测试。

机器可读报告：[2026-10-21-official-combined-strategies.json](2026-10-21-official-combined-strategies.json)。24 个数值对照场景（18 个组合正常/拒绝场景和 6 个方向性复用场景），共比较 130,765 个数组位置（含不可用位置的 NaN 一致性及重复轴比较）。最大绝对误差 0，最大相对误差 0，失败点 0。`rtol=1e-10`、`atol=1e-20`；每个数组保留点数、最大误差及失败坐标字段。

报告保留配置、科学版本、实现身份、源 checksum、实际范围、execution provenance、artifact checksum 和测试/实现代码 checksum。大数组仅在临时 artifact 中；报告不保存原始 EEG、真实身份或本机路径。

## 重跑

从仓库根目录，使用冻结 NumPy 2.3.4、SciPy 1.16.3 的 Python 环境运行：

```powershell
.artifacts/retirement-venv/Scripts/python.exe -m pytest -q backend/tests/test_official_combined_strategies.py backend/tests/test_official_directional_reuse.py backend/tests/test_official_strategy_validation_matrix.py --junitxml=.artifacts/combined-strategy-tests.xml -o junit_family=legacy
.artifacts/retirement-venv/Scripts/python.exe backend/scripts/summarize_strategy_validation.py .artifacts/combined-strategy-tests.xml docs/validation/2026-10-21-official-combined-strategies.json
dotnet test desktop-client/BrainPlatform.Desktop.Tests
```

现有临时隔离环境使用 Python 3.13.3；安装环境细节见退役清理归档报告。重跑报告的临时身份和 artifact checksum 可能变化，应评估当前测试/实现身份与误差，不按报告字节相等判断科学等价。原四个数值/策略基线 JSON/NPZ 文件保持原样；策略矩阵只更新能力证据及 manifest 指纹。

## 限制

本轮未进行独立的桌面点击/视觉验收；自动化测试不能替代该项。测试通过确定性同步控制组合时序，不是负载或 CPU 并行吞吐基准。真实长记录资源、延迟及九算法压力验证未在此轮重新执行。已有真实记录 PSD 组合压力报告继续单独提供证据，不能推广到其他算法或临床结论。
