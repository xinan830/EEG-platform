## Decision

四个 DataGridTextColumn 使用 `StringFormat=F2`，保留原始数值绑定和空值占位；不在 ViewModel 或后端舍入结果。这样仅格式化显示，并保持数值排序和同窗投影。

## Validation And Limits

更新并运行已有 WPF 面板控件测试，检查 OpenSpec 和 diff。两个改动代码文件均低于 400 行。未修改后端，后端测试不适用。低于显示分辨率的小值可能显示为 `0.00`，底层精度不变。
