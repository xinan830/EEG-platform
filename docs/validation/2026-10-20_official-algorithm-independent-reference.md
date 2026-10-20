# 官方算法独立数值参考验证（阶段 3）

日期：2026-10-20<br>
范围：合成信号上的独立数值实现与当前生产实现对照。此报告是工程/数值证据，不是临床或诊断有效性证据。

## 参考实现与冻结条件

- 参考实现：backend/tests/test_official_algorithm_numeric_reference_audit.py，身份 analytic-numpy-reference-v1。参考侧仅用显式 NumPy 数学；生产模块只用于比较 actual，不从生产结果构造 expected。
- PSD/Welch：手写周期 Hann、常量去趋势、单边密度标度与线性功率平均；测试与生产 Welch 共享窗长/步长契约，但不调用 SciPy Welch 生成 reference。
- STFT：手写 4 秒周期 Hann、1 秒步长、单边 FFT 密度和窗口中心时间轴；生产 estimate_spectrogram 仅作 actual。
- Band power：参考侧独立执行边界线性插值与逐梯形求和。
- 版本固定：Python 3.13.9（运行时记录），NumPy 2.3.4，SciPy 1.16.3。测试会拒绝不同的 NumPy/SciPy 版本，以免未经审查地漂移参考数值。
- 比较容差：rtol=1e-10；PSD 密度 atol=1e-24 V²/Hz；无量纲指标和比例 atol=1e-12；FAA 对数差 atol=1e-12。频率轴、时间轴及状态/shape 使用精确比较。

## 覆盖及结果

运行命令：cd backend; python -m pytest tests/test_official_algorithm_numeric_reference_audit.py tests/test_official_algorithm_independent_references.py tests/test_independent_spectral_reference_validation.py -q<br>
结果：14 passed。

| 算法/层 | 对照项 | 结果 |
|---|---|---|
| PSD/Welch | 3 个 4 秒窗、50% 重叠、2 通道；频率轴和 1–30 Hz 矩阵 | 轴精确一致；逐值在冻结容差内 |
| STFT | 5 个窗的中心时间、频率轴和 2 通道功率矩阵 | shape/轴精确一致；逐值在冻结容差内 |
| Peak Frequency | 8–13 Hz 离散最大 bin | 选中 bin 一致 |
| IAPF | OLS 1/f 拟合、peak/COG 与诊断中间量 | source、value、peak、COG、R²、绝对拟合误差在容差内 |
| Band Ratio | 8–13 Hz / 13–30 Hz | 比值及 evidence 中分子功率一致 |
| Theta/Beta | relaxation、spatial distribution、brainbeat、三通道 fatigue、narrow alpha | 所比较指标在容差内 |
| RBP | 五段功率及归一化比例 | 每段中间功率、比例在容差内 |
| FAA | paired epoch FFT；F3/F4 Alpha 功率、clean epoch 数和对数差 | 全部一致 |
| 离线 Brainbeat | 两个独立窗口的 Fz/Pz IAPF 与 Brainbeat；正序/逆序 | 窗口逐个符合独立参考，执行顺序结果不变 |

实际误差来自固定两通道解析合成谱、与测试相同的 8 秒输入。PSD/Welch shape 为 (2,117)，最大绝对误差 1.0339757656912846e-25 V²/Hz，坐标 (channel=0, frequency_index=36)；最大相对误差 1.5729892335485864e-11，坐标 (channel=0, frequency_index=76)。STFT shape 为 (5,2,117)，最大绝对误差 2.0679515313825692e-25 V²/Hz，坐标 (frame=1, channel=0, frequency_index=36)；最大相对误差 1.9214598813192446e-10，坐标 (frame=3, channel=1, frequency_index=3)。STFT 最大相对误差略高于单独的 rtol=1e-10，但该极小功率 bin 的绝对误差仅约 2.07e-25，仍满足混合判据 `abs_error <= atol + rtol*abs(expected)`，故无失败坐标。生产 1e-20 V²/Hz PSD 下限也在独立的 Welch 对照中逐点核验。

ValidationRun 测试会在运行时把 PSD 比较摘要和数组 checksum/shape 写入临时 SQLite evidence；pytest 结束后临时库会清理。本文件以上为可复查的公开合成用例误差摘要。FAA 和复合指标保留逐项容差断言及状态/范围检查，没有各自创建 ValidationRun。

## ValidationRun 与大数组

测试使用 ValidationService 将小摘要写入临时 SQLite ValidationRun，并将频率轴、expected PSD、actual PSD 写入独立 NPZ；证据包含参考实现 ID、运行环境版本、容差、shape、配置哈希、数组 SHA-256、最大误差和坐标。算法比较也验证 clean 状态与请求/实际范围传递。数据由可解析合成正弦生成，dataset identity 仅包含合成数据哈希，无个人/真实记录信息。NPZ 留在测试临时目录，测试结束后由 pytest 清理；没有将大数组或真实 EEG 写入仓库。

该临时运行验证了 ValidationRun 记录与 NPZ 的 checksum/shape/数值可重放关系，但没有持久化到应用正式 ValidationRun 数据库或生产 artifact store。真实记录端到端及授权仍未验证。

## 限制和后续事项

- 对照采用可解析合成信号，不代表 EDF/BDF 真实记录的独立复算结果。
- PSD 生产实际的前置滤波/质量门和 Welch 聚合与原语对照仍依赖当前环境的实现契约；本测试直接为预处理后的有效窗口建立单段/窗序列参考，没有验证完整原始记录滤波链路。
- 刻意选择非整周期振荡以避免当前质量门把重复极值判作 clipping；此处不重新定义 clipping 契约。
- IAPF 非正/非有限 PSD 风险已登记在 OpenSpec design；本阶段不修改生产公式或失败策略。
- 结果不支持任何临床效度、诊断灵敏度/特异度或人群泛化结论。
