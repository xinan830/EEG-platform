# 连续 EEG 波形回放 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 BDF/EDF 导入后的 Vue 波形预览以原桌面端一致的连续滤波和十秒扫屏方式运行。

**Architecture:** 后端独立波形会话在 50ms chunk 上保留显示滤波状态，并通过 WebSocket 发送 µV 样本。前端环形缓冲消费 chunk，Canvas 仅绘制缓冲中的有限值；分析播放服务不改动。

**Tech Stack:** FastAPI、MNE、NumPy、SciPy、Vue 3、TypeScript、Canvas、Vitest、pytest。

**Spec:** `docs/superpowers/specs/2026-09-09-waveform-playback-design.md`

## Global Constraints

- BDF/EDF 导入后可以直接回放，不要求分析通道映射。
- 显示滤波固定为 50Hz 陷波与 1–30Hz 带通，并保留跨 chunk 状态。
- 一次 WebSocket 消息只携带一个 50ms chunk，前端不得把十秒数据一次画出。
- 所有显示通道使用同一微伏标尺与固定垂直偏移。

---

### Task 1: 后端连续显示滤波与通道选择

**Files:**
- Create: `backend/app/services/waveform_playback.py`
- Test: `backend/tests/test_waveform_playback.py`

**Interfaces:**
- Produces: `select_display_channels(names) -> tuple[list[int], list[str]]`
- Produces: `DisplaySignalFilter(sfreq, channel_count).process(chunk) -> np.ndarray`

- [ ] **Step 1: Write failing tests**，验证 O2 可作为 Oz 回退、常量直流输入经过连续滤波后不显示成毫伏级偏置。
- [ ] **Step 2: Run pytest**，确认模块缺失导致失败。
- [ ] **Step 3: Implement** 原桌面端的 DC 估计、50Hz IIR 陷波和 1–30Hz Butterworth 流式滤波。
- [ ] **Step 4: Run pytest**，确认两个行为通过。

### Task 2: 后端波形回放会话和 WebSocket API

**Files:**
- Modify: `backend/app/services/waveform_playback.py`
- Modify: `backend/app/api/playback.py`
- Modify: `backend/app/main.py`
- Test: `backend/tests/test_waveform_playback.py`

**Interfaces:**
- Produces: `WaveformPlaybackService.create(recording_id)`
- Produces: `POST /api/recordings/{recording_id}/waveform-playback`
- Produces: `POST /api/waveform-playback/{session_id}/control`
- Produces: `WS /api/waveform-playback/{session_id}/events`

- [ ] **Step 1: Write failing tests**，验证创建会话不依赖分析映射且控制指令接受 pause、resume、restart、seek、stop。
- [ ] **Step 2: Run pytest**，确认会话/API 未实现导致失败。
- [ ] **Step 3: Implement** 50ms chunk 会话、滤波状态重置和消息队列。
- [ ] **Step 4: Run pytest**，确认会话契约通过。

### Task 3: 前端十秒扫屏缓冲

**Files:**
- Create: `frontend/src/utils/waveformSweepBuffer.ts`
- Test: `frontend/src/utils/waveformSweepBuffer.test.ts`

**Interfaces:**
- Produces: `WaveformSweepBuffer(sfreq, channelNames, windowSeconds)`
- Produces: `push(samples)` 与 `frame()`。

- [ ] **Step 1: Write failing tests**，验证起始位置为空、chunk 只写到扫描指针、扫描头后 0.2 秒为空、写满后时间轴前移十秒。
- [ ] **Step 2: Run Vitest**，确认模块缺失导致失败。
- [ ] **Step 3: Implement** TypedArray 环形缓冲和 NaN 扫描缺口。
- [ ] **Step 4: Run Vitest**，确认扫屏行为通过。

### Task 4: Vue 与波形 WebSocket 集成

**Files:**
- Create: `frontend/src/api/waveformPlayback.ts`
- Modify: `frontend/src/App.vue`
- Modify: `frontend/src/components/WaveformPanel.vue`
- Test: `frontend/src/utils/waveformSweepBuffer.test.ts`

**Interfaces:**
- Consumes: 后端 `info`、`waveform`、`reset`、`completed`、`error` WebSocket 消息。
- Consumes: `WaveformSweepBuffer.frame()`。

- [ ] **Step 1: Add a failing buffer rendering test**，断言 NaN 样本被识别为绘制断点而非 0µV。
- [ ] **Step 2: Run Vitest**，确认断点行为未被覆盖。
- [ ] **Step 3: Implement** 会话创建、WebSocket 消费、暂停/继续/重播/定位控制和 Canvas 有限值分段绘制。
- [ ] **Step 4: Run backend pytest、frontend tests and production build**。

