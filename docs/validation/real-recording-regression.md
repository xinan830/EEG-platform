# 真实记录回归

这是一个本机受控的工程回归工具，用于在算法、导联投影、预处理或 Run 编排改动后重放同一条已授权记录。它检查可复现性和数据链路完整性，不构成临床验证、性能验收或 GUI 验收。

## 运行

在 backend 目录设置本机授权记录路径，然后运行：

    $env:EEG_REGRESSION_RECORDING='已授权记录目录或包含唯一 recordings 子目录的目录'
    ./.venv/Scripts/python.exe -m scripts.real_recording_regression create
    ./.venv/Scripts/python.exe -m scripts.real_recording_regression compare

create 只允许新建基线目录，已有基线会失败，不能覆盖。compare 使用新的临时数据库重放，默认读取 .artifacts/real-recording-regression/baseline/，并写入同目录的报告。也可以用 --recording、--baseline 和 --report 显式指定位置。真实记录、JSON/NPZ 派生结果和报告都被 .gitignore 排除，不能提交到仓库。

## 固定覆盖

工具固定记录开头 30 秒，使用采集默认参考（REF）和由已记录具名电极生成的平均参考（AVG）。九个官方算法各运行静态和固定动态窗口，共 36 个场景；保存完整结果数组、频率/时间轴、窗口状态、质量、失败信息、实际范围、配置快照和来源身份。普通算法、FAA、Brainbeat 使用各自声明的通道角色；Brainbeat 允许明确的 Fz/Pz、Fz-REF/Pz-REF 和 Fz-AVG/Pz-AVG，仍拒绝双极、错序、缺失或重复角色。

独立参考从原始文件重新读取样本，独立执行导联投影、预处理和谱/指标计算；它不导入生产科学公式，也不把生产结果当输入。只有样本、导联投影和科学检查全部通过，才发布基线。

## 结果解释

报告分别给出三类结论：

- independent：原始样本、投影和独立科学参考是否通过；
- identity：生产代码、工具、参考实现、依赖和锁文件身份是否与基线一致；
- comparison：数组、轴、状态、质量、范围、中间量和 provenance 是否在容差内一致。

身份变化和数值变化分开报告。身份变化即使数值未变也会失败，避免把实现变更误认为已经验证。数值差异会记录 rtol/atol、最大绝对/相对误差和前 20 个失败坐标；null、NaN 掩码、dtype、窗口坐标和错误状态也会比较。checksum 用于发现意外篡改，不是防恶意篡改的签名。

当前验收结果：36 个场景、72 个数组；独立核对 6,497,936 个检查点，创建和第二次 compare 均为 0 失败，原始记录目录 checksum/大小前后一致。该结果只证明这条固定片段和两套导联的当前工程链路可重放，不能推广到未覆盖的策略、全记录、其他设备或临床结论。

代码或参考实现发生合法科学变化时，先审阅差异，再使用新的基线目录执行 create；禁止自动接受当前输出，也不要覆盖旧基线。
