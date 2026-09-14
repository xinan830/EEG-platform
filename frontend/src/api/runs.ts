import { request } from './client'

export interface DefinitionMetricRunRequest {
  recordingId: string
  definitionId: string
  definitionVersion: string
  channel: string
  startS: number
  endS: number
  mode?: 'static' | 'dynamic'
  dynamicWindowS?: 10
  refreshStepS?: 1
}

export interface AnalysisRunResponse {
  run_id: string
  status: string
  result_summary?: Record<string, unknown> | null
  error?: { code: string; message: string } | null
}

export function createDefinitionMetricRun(value: DefinitionMetricRunRequest): Promise<AnalysisRunResponse> {
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

export function getRun(runId: string): Promise<AnalysisRunResponse> {
  return request(`/api/runs/${encodeURIComponent(runId)}`)
}
