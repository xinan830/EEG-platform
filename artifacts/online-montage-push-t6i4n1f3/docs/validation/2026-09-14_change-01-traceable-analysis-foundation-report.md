# Change 01 可追溯分析底座验证报告

## 结论

`complete-traceable-analysis-foundation` 在 2026-09-14 完成工程验证：OpenSpec strict validation、116 项后端测试、44 项前端测试、TypeScript 检查、生产构建和 Git whitespace 检查均通过。该结论只证明当前实现满足声明的工程契约，不证明任何 EEG 指标具有临床有效性。

## 范围和环境

| 项目 | 值 |
| --- | --- |
| OpenSpec | 1.8.0 |
| Python | 3.13.3 |
| NumPy | 2.5.3 |
| SciPy | 1.18.1 |
| MNE | 1.12.1 |
| Node.js | 24.11.0 |
| 平台 | Windows，本地单用户工作台 |
| 数据 | 合成数组和临时测试文件；未使用或提交个人 EEG |

## Evidence -> Finding -> Path

### 数据库迁移

**Evidence:** `test_migrations.py` 从无版本 legacy recordings 表升级到 schema v3，重复执行保持 v3；故障注入在建表后抛出异常，表和版本推进均回滚。

**Finding:** 集中迁移具备数据保留、幂等和事务失败回滚能力。

**Path:** `backend/app/persistence/migrations.py` -> `migrate_database()` -> 每版本 `BEGIN IMMEDIATE` -> migration statements -> schema version commit。

### Recording 身份

**Evidence:** `test_recording_service.py` 验证新文件 SHA-256/字节数，验证 legacy 文件可回填，并验证源文件缺失时记录保持可读且身份为 `null`。

**Finding:** 新 Run 可以引用精确源字节；无法恢复的旧源不会被伪造身份或删除记录。

**Path:** `RecordingService` -> `recording_identity.py` -> recordings v2 identity columns -> `RecordingSummary`。

### 缓存和 provenance

**Evidence:** `test_provenance.py` 验证对象键顺序不改变规范摘要、数组顺序会改变摘要、NaN 被拒绝；源摘要、定义摘要、配置摘要、build 和 actual range 任一变化都会改变 cache key。

**Finding:** 等价配置身份稳定，影响结果的五类输入均参与缓存失效。

**Path:** Run request -> normalized config -> `sha256_json()` -> `build_cache_key()` -> `analysis_runs.cache_key`。

### Run 和 artifact

**Evidence:** `test_run_foundation.py` 验证状态转换与终态不可改写；NPZ round trip、shape/unit/index、SHA-256 和篡改检测通过。`test_run_api.py` 验证 Spectrum Run、请求通道顺序、缓存复用和 artifact 查询。

**Finding:** 数组不再塞入 Run JSON，结果可追到不可变 NPZ；缓存命中保留新的 Run 身份并指向原 artifact Run。

**Path:** `/api/runs` -> `RunService` -> `RunRepository` -> `ArtifactStore` -> `storage/artifacts/<run>/<artifact>.npz`。

### 质量失败

**Evidence:** `test_spectral_quality.py` 分别触发 `non_finite`、`amplitude_threshold`、`flatline`、`clipping` 和 `missing_samples`。Run API 测试验证 flatline 结果为 `gate_failed`，PSD/Band Power/RBP 为 `null` 并带结构化原因。

**Finding:** 无效数据不再通过零功率伪装为有效测量，clean-data Welch 数学路径和黄金值保持通过。

**Path:** 4 秒 segment -> `evaluate_spectral_window()` -> clean-only Welch 或 gate failure -> structured Run result/error。

### ValidationRun

**Evidence:** API 测试持久化逐点误差、通过点数和 tolerance，并读取 `engineering-validation-report-v1` 报告；未知 ID 返回 `VALIDATION_NOT_FOUND`。

**Finding:** PASS 的数值依据、环境和工程边界可独立审计，不与临床结论混淆。

**Path:** `/api/validations` -> `ValidationService` -> `validation_runs` -> `/api/validations/{id}/report`。

### 兼容性

**Evidence:** 后端全量 116 项包括现有 recording、mapping、viewer playback、spectrum、spectrogram、analysis、event、audit 和 report 测试；前端 44 项和生产构建通过。

**Finding:** 新资源为增量接入，没有删除现有 API 或把 Viewer 参数混入 Analysis。

**Path:** `app/main.py` 在现有 router 之后注册 Run/Validation router；旧 service 和 route 继续执行原入口。

## 执行命令和结果

```powershell
openspec validate --all --strict --no-interactive
# 8 passed, 0 failed (7 baseline specs + 1 active change)

cd backend
.\.venv\Scripts\python.exe -m pytest
# 116 passed, 2 dependency deprecation warnings

cd ..\frontend
npm test -- --run
# 44 passed

npx vue-tsc --noEmit
# passed

npm run build
# passed

cd ..
git diff --check
# passed; only Git CRLF conversion notices
```

两条 Python warning 来自 FastAPI/Starlette TestClient 对未来 `httpx2` 和 AnyIO alias 的弃用提示，不是测试失败，也不影响当前运行结果。

## 数值回归

现有 clean 合成数据黄金断言继续通过：

```text
F3 alpha band power = 49.99998294 uV^2
Fz theta band power = 32.00000006 uV^2
F3 alpha relative power = 0.9999992453
```

10 Hz 已知振幅正弦理论功率、PSD 非负、RBP、通道顺序和单窗口 Spectrogram/PSD 逐点一致测试均包含在通过的后端套件中。

## 残余风险和边界

- `POST /api/runs` 当前同步执行并返回 201，不是持久异步队列；进程重启恢复、运行中取消和 202 语义属于 Change 06。
- Recording legacy backfill 首次启动需要读取缺失摘要的大文件，数据量大时会增加首次启动时间。
- NPZ 适合平台内部数值保存，但第三方可移植 CSV/JSON manifest 导出属于 Change 07。
- clipping 使用重复极值的工程检测规则，不等同于设备厂商的 ADC 饱和定义；阈值已写入算法契约和 Run provenance。
- build identity 在开发环境默认来自包版本；产生可能影响数值的发布修复时，发布流程必须显式设置唯一 `BRAIN_PLATFORM_BUILD_VERSION`。
- 本报告不包含真实 BDF/EDF 或身份信息，也不替代真实本地数据的后续 shadow/golden 验证。
