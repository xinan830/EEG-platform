# WPF 参数与交互自动化验收

本轮完成桌面自动化回归和参数校验修复，**不等于真实 WPF 窗口的完整人工验收**。
未修改科学算法、图表样式、原始记录或正常应用数据库，也未提交推送。

## 结果

- 修改前桌面测试：385 通过。
- 修改后桌面测试：407 通过，0 失败，0 跳过。
- 新增 22 项测试，覆盖目录上下限、HTTP 错误形态、陷波配置序列化、
  不提交错误 Run，以及动态时间拖动/播放/暂停/步进。
- OpenSpec 严格校验、文件规模检查、git diff --check 通过。
- 存在已有 NU1701 警告：OpenTK、OpenTK.GLWpfControl、SkiaSharp.Views.WPF
  的还原目标与 net10.0-windows 不完全匹配。测试通过不消除兼容性风险。

## 证据、发现和修复路径

### 参数上下限没有用于提交前校验

证据：AlgorithmParameter 已包含 Minimum/Maximum，但 ExecuteRunAsync 只调用模块
解析和 Nyquist 校验；未使用目录声明的数值范围。

发现：峰频率、频段功率比的不支持频段会被提交给后端，再收到拒绝。
后端拒绝是正确行为，但前端可以利用现有目录更早说明输入问题。

路径：新增 AlgorithmParameterValidation，读取后端目录字段，校验实际序列化 config。
ExecuteRunAsync 在 ClearPreviews 和 HTTP 请求之前调用该校验。
提示包含中文字段名、允许边界、当前输入和单位。没有按算法 ID 写死 1–30 Hz。
后端仍负责权威验证；省略/null 参数继续由后端默认值处理。

### HTTP 错误处理存在二次异常风险

证据：HttpAlgorithmClient 对 detail 直接 GetString，数组/对象会触发类型异常。
这是静态审查与新增响应模拟测试确认的缺陷，不宣称在当前正常后端响应中已复现。

路径：按 JSON 类型读取 code/message/detail；列表和对象保留详细内容，
非 JSON、非对象或未知形态保留中文 HTTP 状态提示和稳定错误码。
任意英文后端科学描述没有通过猜测改译。

### 动态操作自动化证据

PSD 测试使用模拟后端预览，将进度拖到 17 秒，验证对应谱数据；播放再暂停保留游标；
步进到拒绝窗口清空谱图，拖到下一完整窗口恢复数据。
模拟客户端的创建/轮询方法直接抛异常，因此测试也验证这些操作不提交新的科学 Run。
该 fixture 没有注册记录，时间总长使用最后窗口 149 秒；真实记录时长不是这个假设。

## 重现命令

在仓库根目录执行：

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj --no-restore --verbosity minimal
openspec validate validate-wpf-algorithm-parameters --strict
git diff --check
```

在 backend 目录执行：

```powershell
uv run --frozen --extra dev python scripts/check_file_sizes.py
```

## 尚未验收

- 真实窗口里的鼠标拖动、播放时序、图表悬停和缩放。
- 使用用户真实记录切换通道后，端到端保持游标并展示新通道结果。
- 后端目录实时载入后，中文校验提示在运行卡上的实际可见效果。
- PSD/STFT 的关闭/50/60 Hz 陷波在真实页面的溯源展示；本轮只验证请求序列化，
  后端数值与快照检查见此前算法审计报告。

这些项目不能从模拟客户端或后端 HTTP 成功推断为已通过。
