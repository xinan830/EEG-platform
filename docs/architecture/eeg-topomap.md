# EEG 地形图能力边界

地形图是算法逐通道结果的空间投影，不是新的 EEG 算法。算法适配器只提供频段/指标、逐通道值、单位、窗口和 provenance；后端 `TopomapService` 复用已注册的电极位置，执行空间资格校验和有界确定性插值。WPF 只渲染后端返回的点与网格，不计算功率、频段或插值。

数据回放使用统一时间协调器的按需窗口身份。动态窗口通过 `/api/analysis-windows/{window_id}/topomap` 获取，完整 Run 通过 `/api/runs/{run_id}/topomap` 获取；旧窗口响应由 Review 分析协调器丢弃。当前只注册 RBP 的 Delta、Theta、Alpha、Beta、Gamma。统一录音访问层物化 montage 快照中的 `MapX/MapY`；手动位置优先，历史快照缺少位置时由同一标准模板解析器补齐并写入 provenance。动态地形图直接消费同一窗口科学配置中已校验的 signal source 和 montage 身份，不在每次刷新时重新查询录音目录或解析 montage；地形图不维护独立位置解析路径。
