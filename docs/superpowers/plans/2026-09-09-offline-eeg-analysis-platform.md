# 离线脑电分析平台 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个以 FastAPI 为算法权威、Vue 为浏览器工作台的 BDF/EDF 离线脑电导入、分析与回放平台。

**Architecture:** FastAPI 接收、验证并保存录制文件，SQLite 保存目录、通道映射和分析资源；Python 处理层从桌面端迁移纯算法逻辑，输出以 `elapsed_s` 为统一时间轴的 JSON 结果。Vue 通过类型化 API 读取资源，完成导入、映射、分析与可控回放，不在浏览器中重复任何 EEG 计算。

**Tech Stack:** Python 3.10+、FastAPI、MNE、NumPy、SciPy、specparam、SQLite、pytest、Vue 3、TypeScript、Vite、Apache ECharts。

**Spec:** `docs/superpowers/specs/2026-09-09-offline-eeg-analysis-platform-design.md`

## Global Constraints

- 仅支持 BDF/EDF 文件；原文件存放在 `storage/recordings/`，不进入版本控制。
- Fz、Pz、Oz 必须由用户显式映射；F3/F4 仅用于可选 FAA，绝不按列位置推断电极。
- 所有趋势和事件都使用样本数据时间 `elapsed_s`，而非浏览器墙上时钟。
- IAPF 与指标计算必须与 `D:\AI\Work\eeg-process-main` 的当前实现口径一致；源项目本身不修改。
- 本阶段不接入 eego USB、FreeBCI 串口、LSL 或阻抗检测。
- 新增后端行为先写 pytest；新增前端状态与 API 行为先写测试或可执行的 TypeScript 类型检查，再写实现。
- 目标目录当前不是 Git 仓库；每个任务的“提交”步骤改为记录可提交文件列表，待用户初始化仓库后再以中文提交信息提交。

---

## 文件结构

```text
backend/
├── app/
│   ├── api/{recordings,analyses}.py
│   ├── core/config.py
│   ├── models/{recording,analysis}.py
│   ├── processing/{iapf_estimator,metrics,offline_analysis}.py
│   ├── services/{recordings,analysis}.py
│   └── main.py
├── tests/{conftest,test_health,test_recordings,test_mapping,test_analysis}.py
└── pyproject.toml
frontend/
├── src/
│   ├── api/{client,recordings,analyses}.ts
│   ├── components/{FileImport,ChannelMapping,WaveformPanel,ScorePanel,AnalysisTabs}.vue
│   ├── composables/usePlayback.ts
│   ├── types/{recording,analysis}.ts
│   ├── views/{LibraryView,WorkbenchView}.vue
│   ├── App.vue
│   └── styles.css
└── package.json
storage/recordings/
```

### Task 1: 建立可重复的空项目基线

**Files:**
- Create: `backend/pyproject.toml`
- Create: `backend/app/__init__.py`
- Create: `backend/app/main.py`
- Create: `backend/tests/test_health.py`
- Create: `frontend/package.json`
- Create: `frontend/index.html`
- Create: `frontend/src/main.ts`
- Create: `frontend/src/App.vue`
- Create: `.gitignore`
- Modify: `README.md`

**Interfaces:**
- Produces: `GET /api/health -> {"status": "ok", "service": "brain-platform-backend"}`。
- Produces: Vue 开发服务器默认运行于 `http://127.0.0.1:5173`。

- [ ] **Step 1: 写健康检查的失败测试**

```python
from fastapi.testclient import TestClient

from app.main import app


def test_health_reports_backend_identity():
    response = TestClient(app).get('/api/health')
    assert response.status_code == 200
    assert response.json() == {'status': 'ok', 'service': 'brain-platform-backend'}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd backend; uv run pytest tests/test_health.py -v`

Expected: 因 `app.main` 尚不存在而失败。

- [ ] **Step 3: 建立最小 FastAPI 应用与前端脚手架**

```python
# backend/app/main.py
from fastapi import FastAPI

app = FastAPI(title='Brain Platform API', version='0.1.0')

@app.get('/api/health')
def health() -> dict[str, str]:
    return {'status': 'ok', 'service': 'brain-platform-backend'}
```

```json
// frontend/package.json 的 scripts
{"dev":"vite","build":"vue-tsc --noEmit && vite build"}
```

- [ ] **Step 4: 验证后端与前端基线**

Run: `cd backend; uv run pytest tests/test_health.py -v; cd ../frontend; npm run build`

Expected: 健康测试和 Vue 生产构建均通过。

- [ ] **Step 5: 记录待提交内容**

```text
backend/pyproject.toml, backend/app/, backend/tests/test_health.py,
frontend/, .gitignore, README.md
建议中文提交信息：初始化离线脑电分析平台
```

### Task 2: 录制目录、SQLite 与文件安全边界

**Files:**
- Create: `backend/app/core/config.py`
- Create: `backend/app/models/recording.py`
- Create: `backend/app/services/recordings.py`
- Create: `backend/tests/test_recording_service.py`

**Interfaces:**
- Produces: `RecordingService.create_recording(original_name, suffix, raw_bytes) -> RecordingSummary`。
- Produces: `RecordingSummary(id, original_name, stored_name, extension, created_at, sfreq, duration_s, channels, mapping)`。

- [ ] **Step 1: 写文件名隔离的失败测试**

```python
from pathlib import Path
from app.services.recordings import RecordingService


def test_create_recording_never_uses_user_filename_as_storage_path(tmp_path: Path):
    service = RecordingService(storage_dir=tmp_path / 'recordings', database_path=tmp_path / 'catalog.sqlite3')
    item = service.create_recording('../../unsafe.bdf', '.bdf', b'raw')
    assert item.stored_name.endswith('.bdf')
    assert '..' not in item.stored_name
    assert (tmp_path / 'recordings' / item.stored_name).read_bytes() == b'raw'
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd backend; uv run pytest tests/test_recording_service.py::test_create_recording_never_uses_user_filename_as_storage_path -v`

Expected: 因服务不存在而失败。

- [ ] **Step 3: 实现配置、表初始化和安全 UUID 文件名**

```python
def create_recording(self, original_name: str, suffix: str, raw_bytes: bytes) -> RecordingSummary:
    if suffix.lower() not in {'.bdf', '.edf'}:
        raise ValueError('仅支持 BDF 或 EDF 文件')
    recording_id = uuid4().hex
    stored_name = f'{recording_id}{suffix.lower()}'
    (self.storage_dir / stored_name).write_bytes(raw_bytes)
    return self._insert_pending_recording(recording_id, original_name, stored_name, suffix.lower())
```

- [ ] **Step 4: 扩展服务测试并验证**

Run: `cd backend; uv run pytest tests/test_recording_service.py -v`

Expected: 创建、查询、缺失 ID 与非法后缀均通过明确断言。

- [ ] **Step 5: 记录待提交内容**

```text
backend/app/core/config.py, backend/app/models/recording.py,
backend/app/services/recordings.py, backend/tests/test_recording_service.py
建议中文提交信息：建立录制文件目录与安全存储
```

### Task 3: BDF/EDF 导入 API 与通道映射

**Files:**
- Create: `backend/app/api/recordings.py`
- Create: `backend/tests/test_recordings_api.py`
- Create: `backend/tests/test_mapping.py`
- Modify: `backend/app/main.py`
- Modify: `backend/app/services/recordings.py`

**Interfaces:**
- Consumes: `RecordingService`。
- Produces: `POST /api/recordings/import`、`GET /api/recordings`、`GET /api/recordings/{id}`、`PUT /api/recordings/{id}/mapping`。
- Produces: `ChannelMapping(fz: str, pz: str, oz: str, f3: str | None, f4: str | None)`。

- [ ] **Step 1: 写核心通道映射的失败测试**

```python
def test_mapping_requires_distinct_fz_pz_oz(client, recording_id):
    response = client.put(f'/api/recordings/{recording_id}/mapping', json={
        'fz': 'C1', 'pz': 'C1', 'oz': 'C3', 'f3': None, 'f4': None,
    })
    assert response.status_code == 422
    assert '重复' in response.json()['detail']
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd backend; uv run pytest tests/test_mapping.py::test_mapping_requires_distinct_fz_pz_oz -v`

Expected: 路由不存在而失败。

- [ ] **Step 3: 实现文件探测和映射验证**

```python
def validate_mapping(mapping: ChannelMapping, available: list[str]) -> ChannelMapping:
    required = [mapping.fz, mapping.pz, mapping.oz]
    if len({name.upper() for name in required}) != 3:
        raise ValueError('Fz、Pz、Oz 映射不能重复')
    if any(name not in available for name in required):
        raise ValueError('映射通道不存在于录制文件')
    return mapping
```

- [ ] **Step 4: 验证 API 端到端行为**

Run: `cd backend; uv run pytest tests/test_recordings_api.py tests/test_mapping.py -v`

Expected: 非 BDF/EDF、损坏文件、重复映射、缺少核心映射均为 4xx；有效映射可持久查询。

- [ ] **Step 5: 记录待提交内容**

```text
backend/app/api/recordings.py, backend/app/main.py,
backend/app/services/recordings.py, backend/tests/test_recordings_api.py,
backend/tests/test_mapping.py
建议中文提交信息：支持脑电文件导入与通道映射
```

### Task 4: 迁移可复现的 IAPF 与离线指标纯算法

**Files:**
- Create: `backend/app/models/analysis.py`
- Create: `backend/app/processing/iapf_estimator.py`
- Create: `backend/app/processing/metrics.py`
- Create: `backend/app/processing/offline_analysis.py`
- Create: `backend/tests/test_iapf_estimator.py`
- Create: `backend/tests/test_offline_analysis.py`

**Interfaces:**
- Produces: `IAPFEstimator.compute(buffer, label, duration_s) -> CalibrationResult`。
- Produces: `analyze_recording(data, sfreq, mapping, events) -> AnalysisResult`。
- Produces: 每个趋势点都有 `elapsed_s`，每个不可用值有原因。

- [ ] **Step 1: 写 30 秒窗口和 5 秒步长的失败测试**

```python
def test_offline_analysis_emits_iapf_attempts_every_five_seconds_after_thirty_seconds():
    result = analyze_recording(alpha_fixture(seconds=45, sfreq=100), 100, mapping(), [])
    assert [point.elapsed_s for point in result.iapf_attempts] == [30.0, 35.0, 40.0, 45.0]
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd backend; uv run pytest tests/test_offline_analysis.py::test_offline_analysis_emits_iapf_attempts_every_five_seconds_after_thirty_seconds -v`

Expected: 分析模块不存在而失败。

- [ ] **Step 3: 从源项目迁移纯计算逻辑，不迁移 Qt 或多进程代码**

```python
for end_sample in range(window_samples, total_samples + 1, attempt_samples):
    window = data[end_sample - window_samples:end_sample]
    calibration = estimator.compute(window, 'lock', duration_s=30.0)
    attempts.append(IAPFAttempt(elapsed_s=end_sample / sfreq, result=calibration))
```

实现时逐项迁移以下规则：MNE notch/bandpass、2 秒 150 µV 伪迹窗、短干净段吸收、Welch 4 秒/50% overlap、R²/MAE 总门、显著峰优先与 CoG 兜底、三次候选中位数锁定、FAA 的 2 秒/50% epoch 以及 `ln(P_F4)-ln(P_F3)`。

- [ ] **Step 4: 验证算法回归**

Run: `cd backend; uv run pytest tests/test_iapf_estimator.py tests/test_offline_analysis.py -v`

Expected: 合成 alpha 峰、低质量窗、峰缺失 CoG 兜底、锁定中位数、FAA 缺失 F3/F4 与数据时间轴均通过。

- [ ] **Step 5: 记录待提交内容**

```text
backend/app/models/analysis.py, backend/app/processing/, backend/tests/test_iapf_estimator.py,
backend/tests/test_offline_analysis.py
建议中文提交信息：迁移离线脑电分析算法
```

### Task 5: 分析资源 API 与结果持久化

**Files:**
- Create: `backend/app/api/analyses.py`
- Create: `backend/app/services/analysis.py`
- Create: `backend/tests/test_analysis_api.py`
- Modify: `backend/app/services/recordings.py`
- Modify: `backend/app/main.py`

**Interfaces:**
- Consumes: `analyze_recording` 与已保存的 `ChannelMapping`。
- Produces: `POST /api/recordings/{id}/analysis -> {analysis_id, status, result_url, summary}`。
- Produces: `GET /api/analyses/{analysis_id} -> AnalysisResult`。

- [ ] **Step 1: 写分析资源的失败测试**

```python
def test_analysis_creates_a_queryable_result_resource(client, mapped_recording_id):
    created = client.post(f'/api/recordings/{mapped_recording_id}/analysis')
    assert created.status_code == 201
    analysis_id = created.json()['analysis_id']
    result = client.get(f'/api/analyses/{analysis_id}')
    assert result.status_code == 200
    assert result.json()['recording_id'] == mapped_recording_id
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd backend; uv run pytest tests/test_analysis_api.py::test_analysis_creates_a_queryable_result_resource -v`

Expected: 分析 API 不存在而失败。

- [ ] **Step 3: 实现同步编排与结果 JSON 存储**

```python
def create_analysis(self, recording_id: str) -> AnalysisSummary:
    recording = self.recordings.require_mapped_recording(recording_id)
    result = analyze_recording(self.recordings.load_data(recording), recording.sfreq, recording.mapping, recording.events)
    analysis_id = uuid4().hex
    self._save_result(analysis_id, recording_id, result.model_dump(mode='json'))
    return AnalysisSummary(analysis_id=analysis_id, status='completed', result_url=f'/api/analyses/{analysis_id}')
```

- [ ] **Step 4: 验证完整分析生命周期**

Run: `cd backend; uv run pytest tests/test_analysis_api.py -v`

Expected: 未映射录制返回 409、未知录制/分析 ID 返回 404、完成分析结果包含谱与数据时间序列。

- [ ] **Step 5: 记录待提交内容**

```text
backend/app/api/analyses.py, backend/app/services/analysis.py,
backend/app/services/recordings.py, backend/app/main.py, backend/tests/test_analysis_api.py
建议中文提交信息：提供离线分析结果接口
```

### Task 6: Vue 类型化 API 与导入/映射流程

**Files:**
- Create: `frontend/src/types/recording.ts`
- Create: `frontend/src/types/analysis.ts`
- Create: `frontend/src/api/client.ts`
- Create: `frontend/src/api/recordings.ts`
- Create: `frontend/src/api/analyses.ts`
- Create: `frontend/src/components/FileImport.vue`
- Create: `frontend/src/components/ChannelMapping.vue`
- Create: `frontend/src/views/LibraryView.vue`
- Create: `frontend/src/views/WorkbenchView.vue`
- Modify: `frontend/src/App.vue`

**Interfaces:**
- Consumes: `/api/recordings` 和 `/api/analyses` 资源。
- Produces: `recording-selected`、`mapping-saved`、`analysis-ready` 前端事件。

- [ ] **Step 1: 写类型化请求边界的失败测试**

```ts
import { normalizeApiError } from '../src/api/client'

it('keeps FastAPI detail text for form errors', () => {
  expect(normalizeApiError({ detail: 'Fz、Pz、Oz 映射不能重复' })).toBe('Fz、Pz、Oz 映射不能重复')
})
```

- [ ] **Step 2: 运行测试或类型检查确认失败**

Run: `cd frontend; npm run build`

Expected: 因 API 客户端、组件或类型不存在而失败。

- [ ] **Step 3: 实现导入、录制列表和映射表单**

```ts
export async function saveMapping(id: string, mapping: ChannelMapping): Promise<Recording> {
  return request<Recording>(`/api/recordings/${id}/mapping`, {
    method: 'PUT',
    body: JSON.stringify(mapping),
  })
}
```

映射 UI 必须将核心 Fz/Pz/Oz 标为必填，F3/F4 标为 FAA 可选，后端报错直接显示而不在前端猜测通道。

- [ ] **Step 4: 验证前端构建**

Run: `cd frontend; npm run build`

Expected: TypeScript 与 Vite 构建通过，无隐式 `any` 或未使用 API。

- [ ] **Step 5: 记录待提交内容**

```text
frontend/src/types/, frontend/src/api/, frontend/src/components/FileImport.vue,
frontend/src/components/ChannelMapping.vue, frontend/src/views/, frontend/src/App.vue
建议中文提交信息：实现录制导入与通道映射界面
```

### Task 7: 分析工作台、数据时间回放与图表

**Files:**
- Create: `frontend/src/composables/usePlayback.ts`
- Create: `frontend/src/components/WaveformPanel.vue`
- Create: `frontend/src/components/ScorePanel.vue`
- Create: `frontend/src/components/AnalysisTabs.vue`
- Create: `frontend/src/components/MetricChart.vue`
- Modify: `frontend/package.json`
- Modify: `frontend/src/views/WorkbenchView.vue`
- Modify: `frontend/src/styles.css`

**Interfaces:**
- Consumes: `AnalysisResult` 的 `elapsed_s`、波形抽样点、指标趋势、PSD、时段报告。
- Produces: `usePlayback(durationS)`，包含 `isPlaying`、`speed`、`positionS`、`play()`、`pause()`、`restart()`。

- [ ] **Step 1: 写倍速不改变数据时间的失败测试**

```ts
it('changes display progression without changing source time values', () => {
  const playback = createPlayback(120)
  playback.setSpeed(4)
  playback.seek(30)
  expect(playback.positionS.value).toBe(30)
  expect(playback.sourceElapsedS(30)).toBe(30)
})
```

- [ ] **Step 2: 运行测试或构建确认失败**

Run: `cd frontend; npm run build`

Expected: 回放组合式函数和图表组件尚不存在而失败。

- [ ] **Step 3: 实现工作台与图表依赖**

```ts
export function usePlayback(durationS: number) {
  const positionS = ref(0)
  const speed = ref<1 | 2 | 4>(1)
  const visible = <T extends { elapsed_s: number }>(points: T[]) =>
    points.filter((point) => point.elapsed_s <= positionS.value)
  return { positionS, speed, visible }
}
```

添加 `echarts` 依赖。工作台显示：录制名称与播放控件、带事件线的波形、综合分与实时/锁定 IAPF、PSD/1-f/IAPF 图、RBP/疲劳/HAI/1-f/FAA 标签页。无数据一律展示原因文案，不绘制零线。

- [ ] **Step 4: 验证工作台构建**

Run: `cd frontend; npm install; npm run build`

Expected: ECharts、回放状态和所有 Vue 组件经 TypeScript 检查后成功打包。

- [ ] **Step 5: 记录待提交内容**

```text
frontend/package.json, frontend/src/composables/usePlayback.ts,
frontend/src/components/{WaveformPanel,ScorePanel,AnalysisTabs,MetricChart}.vue,
frontend/src/views/WorkbenchView.vue, frontend/src/styles.css
建议中文提交信息：构建脑电分析回放工作台
```

### Task 8: 全栈验收、运行文档与旧缓存隔离

**Files:**
- Create: `backend/tests/test_end_to_end_api.py`
- Modify: `README.md`
- Modify: `.gitignore`
- Modify: `backend/pyproject.toml`

**Interfaces:**
- Consumes: 录制导入、映射、分析 API。
- Produces: 可复制的本地启动命令和一次完整离线分析的验收路径。

- [ ] **Step 1: 写端到端 API 的失败测试**

```python
def test_import_mapping_and_analysis_lifecycle(client, bdf_upload):
    imported = client.post('/api/recordings/import', files={'file': bdf_upload})
    recording_id = imported.json()['id']
    client.put(f'/api/recordings/{recording_id}/mapping', json=valid_mapping())
    created = client.post(f'/api/recordings/{recording_id}/analysis')
    assert client.get(created.json()['result_url']).json()['status'] == 'completed'
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd backend; uv run pytest tests/test_end_to_end_api.py -v`

Expected: 在全链路任一未实现环节失败，修复前不得放宽断言。

- [ ] **Step 3: 补齐运行文档与忽略规则**

```markdown
cd backend
uv sync --extra dev
uv run uvicorn app.main:app --reload --port 8000

cd ../frontend
npm install
npm run dev
```

`.gitignore` 必须忽略 `storage/recordings/`、`storage/*.sqlite3`、`backend/.venv/`、`frontend/node_modules/`、构建产物和日志；不忽略源代码、锁文件和设计文档。

- [ ] **Step 4: 运行完整验证**

Run: `cd backend; uv run pytest -q; cd ../frontend; npm run build`

Expected: 后端测试全绿，前端类型检查与生产构建全绿。

- [ ] **Step 5: 人工验收与记录待提交内容**

```text
1. 启动后端和前端。
2. 导入一份 BDF/EDF，完成 Fz/Pz/Oz 映射。
3. 执行分析，检查综合分、IAPF、PSD、趋势和 FAA 缺失状态。
4. 在 1x、2x、4x 下比较同一条结果的 elapsed_s。
建议中文提交信息：完成离线脑电分析平台验收
```

## 计划自检

- 规格覆盖：任务 1 建基线；任务 2–3 覆盖安全导入与映射；任务 4–5 覆盖算法与资源 API；任务 6–7 覆盖 Vue 流程和可视化；任务 8 覆盖全栈验收与文档。
- 占位符扫描：不含 TBD、TODO 或“稍后实现”类占位。
- 接口一致性：后端以 `RecordingService`、`ChannelMapping`、`AnalysisResult` 为边界；前端只消费 API 模型和 `elapsed_s`，不调用算法代码。
