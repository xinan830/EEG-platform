import { request } from './client'

export interface DefinitionMetricRunRequest {
  recordingId: string
  definitionId: string
  definitionVersion: string
  channel: string
  startS: number
  endS: number
}

export interface AnalysisRunResponse {
  run_id: string
  status: string
  result_summary?: Record<string, unknown> | null
  error?: { code: string; message: string } | null
}

export function createDefinitionMetricRun(value: DefinitionMetricRunRequest): Promise<AnalysisRunResponse> {
  return request('/api/runs', {
    method: 'POST',
    body: JSON.stringify({
      recording_id: value.recordingId,
      analysis_type: 'definition_metric',
      definition_id: value.definitionId,
      definition_version: value.definitionVersion,
      config: { channel: value.channel, time: { start_s: value.startS, end_s: value.endS } },
    }),
  })
}

export function getRun(runId: string): Promise<AnalysisRunResponse> {
  return request(`/api/runs/${encodeURIComponent(runId)}`)
}
