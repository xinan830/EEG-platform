import { request } from './client'
import type { DynamicWindowS } from '../utils/dynamicMetricPlayback'
import type { AnalysisProvenance } from '../types/analysisProvenance'

type SharedAlgorithmRunFields = {
  recordingId: string
  channel: string
  startS: number
  endS: number
  mode?: 'static' | 'dynamic'
  dynamicWindowS?: DynamicWindowS
  refreshStepS?: 1
}

export type AlgorithmRunRequest =
  | (SharedAlgorithmRunFields & { source: 'user'; definitionId: string; definitionVersion: string })
  | (SharedAlgorithmRunFields & { source: 'official'; algorithmId: string })

export interface AnalysisRunResponse {
  run_id: string
  status: string
  definition_id?: string | null
  definition_version?: string | null
  scientific_version?: string
  implementation_version?: string
  config_sha256?: string
  requested_range?: { start_s: number; end_s: number }
  actual_range?: { start_s: number; end_s: number } | null
  channel_mapping?: Record<string, unknown>
  reference?: Record<string, unknown>
  filters?: Record<string, unknown>
  window?: Record<string, unknown>
  quality_rules?: Record<string, unknown>
  environment?: Record<string, string>
  result_summary?: Record<string, unknown> | null
  error?: { code: string; message: string } | null
  analysis_provenance?: AnalysisProvenance
}

export function createAlgorithmRun(value: AlgorithmRunRequest): Promise<AnalysisRunResponse> {
  return value.source === 'official' ? createOfficialRun(value) : createDefinitionRun(value)
}

function createDefinitionRun(value: Extract<AlgorithmRunRequest, { source: 'user' }>): Promise<AnalysisRunResponse> {
  const config: Record<string, unknown> = { channel: value.channel, time: { start_s: value.startS, end_s: value.endS } }
  if (value.mode === 'dynamic') {
    config.mode = 'dynamic'
    config.dynamic_window_s = value.dynamicWindowS ?? 10
    config.refresh_step_s = value.refreshStepS ?? 1
  }
  return request('/api/runs', {
    method: 'POST',
    body: JSON.stringify({
      recording_id: value.recordingId,
      analysis_type: 'definition_metric',
      definition_id: value.definitionId,
      definition_version: value.definitionVersion,
      config,
    }),
  })
}

function createOfficialRun(value: Extract<AlgorithmRunRequest, { source: 'official' }>): Promise<AnalysisRunResponse> {
  const config: Record<string, unknown> = {
    algorithm_id: value.algorithmId,
    time: { start_s: value.startS, end_s: value.endS },
    mode: value.mode ?? 'static',
  }
  config.channel = value.channel
  if (value.mode === 'dynamic') {
    config.dynamic_window_s = value.dynamicWindowS ?? 10
    config.refresh_step_s = value.refreshStepS ?? 1
  }
  return request('/api/runs', {
    method: 'POST',
    body: JSON.stringify({ recording_id: value.recordingId, analysis_type: 'official_algorithm', config }),
  })
}

export function getRun(runId: string): Promise<AnalysisRunResponse> {
  return request(`/api/runs/${encodeURIComponent(runId)}`)
}
