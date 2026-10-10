# 导联颜色、顺序与公共颜色控件验收

日期：2026-10-10。范围仅为桌面展示契约，不改变 EEG 样本和科学算法。

## 变更与依据

| 发现 | 实现路径 | 验证证据 |
| --- | --- | --- |
| 事件页面独占色块、RGB 弹窗和调色回调 | Shared/Controls/ColorPicker；事件绑定 SelectedColor | ColorPickerTests；EventDefinitionEditorViewTests |
| 导联顺序只读，非稀疏顺序被绑定源通道顺序 | MontageOutputOrder；MontageValidation | MontageAppearanceTests：排序、唯一性、系统只读、保存重开 |
| 两个 SciChart 使用按行号的私有调色板 | WaveformTraceColors；live trace/review frame 显式颜色 | MontageAppearanceProjectionTests：颜色、顺序、真实源、旧快照 |
| 导联详情表含冗余负极输入编号 | 独立 MontageOutputTable 替换原内联表 | MontageOutputTableTests：真实 XAML 绑定、按钮、只读、截图 |

计划和迁移边界见
[design.md](../../openspec/changes/customize-montage-presentation/design.md)。

## 自动验证

所有 .NET 构建使用 `_verify/montage-presentation` 隔离产物，不覆盖用户运行的程序。

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --artifacts-path _verify/montage-presentation --verbosity quiet -p:NoWarn=NU1701
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --configuration Release --artifacts-path _verify/montage-presentation --verbosity quiet -p:NoWarn=NU1701
backend/.venv/Scripts/python.exe -m pytest backend/tests/test_acquisition_analysis_signals.py backend/tests/test_wpf_recording.py backend/tests/test_montage.py -q
openspec validate customize-montage-presentation --strict
backend/.venv/Scripts/python.exe backend/scripts/check_file_sizes.py
git -c core.safecrlf=false diff --check
```

结果在最终验证后记录到本变更 tasks.md。后端当前通过 46 项；其两个既有
Starlette/anyio 弃用警告与本次颜色、排序无关。后端生产代码未修改。
桌面超限文件逐一按实际 Modules 路径登记并检查。

自动化覆盖：颜色双向绑定、外部更新、HEX/RGB/预设选择、取消不提交、无效颜色、
禁用关闭弹窗、颜色规范化、复制配置、系统只读、保存重开、旧 JSON、旧指纹、
严格快照来源、隐藏通道、同名新颜色、REF/双极/平均/双参考数值不变。

## 布局证据

通过真实 WPF XAML 测试生成 900/1200 DIP 宽截图并检查表头、行和按钮排列。
设置 `BRAIN_PLATFORM_UI_EVIDENCE` 后运行 MontageOutputTableTests 可重复生成。
本地截图为 `_verify/montage-presentation/visual-evidence/montage-table-900.png`
和 `montage-table-1200.png`，仅含合成 F3/F7，不含真实 EEG 或个人信息，不提交产物。

## 人工验收

1. 重新运行桌面项目，复制系统导联；确认负极输入编号列消失，参考选择仍可用。
2. 给两条导联选不同颜色，上下移交换顺序；保存后重开，颜色跟随导联而非行号。
3. 选此导联进行设备预览/采集，确认标签、波形颜色、上下顺序和物理输入对应。
4. 完成记录并回溯，默认颜色、顺序与采集一致；切换同通道配置的另一导联正常。
5. 修改原预设后再次打开旧记录，默认仍展示采集快照，不被后来修改覆盖。
6. 事件编辑验证色块、自定义 RGB、取消、HEX 和保存；系统导联只能复制后修改。

未执行真实 ANT 设备采集或 SciChart GPU 画面验收。WPF 表格截图和合成信号测试
证明绑定与投影契约，不代表硬件持续采集已经验收。

## 规模与回滚

详情 XAML 从 470 行降低到 358 行；颜色交互、表格、草稿构建和排序分别有明确
模块所有者。现有较大生命周期和图表宿主仍登记拆分评估，不声称全项目已无超限文件。
不修改历史原始记录。旧客户端可忽略新增 Color 字段，但可能不支持独立输出顺序；
回滚需要保留新保存配置的备份，不应通过重写历史快照来恢复旧 UI。
