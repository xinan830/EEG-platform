import { apiBase, request } from './client'

export interface RunSummary { run_id: string; recording_id: string; analysis_type: string; status: string; scientific_version: string; implementation_version: string; result_summary: Record<string, unknown> | null; error: { code: string; message: string } | null }
export interface ResultView { run: RunSummary; artifacts: Array<{ artifact_id: string; unit: string | null; shape: Record<string, number[]>; sha256: string }>; data_classification: { measured_data: boolean; algorithm_output: boolean; clinical_interpretation: boolean } }
export interface StructuredPreview {
  run_id: string
  artifact: { artifact_id: string; sha256: string; shape: Record<string, number[]>; unit: string | null }
  output: { id: string; label: string; kind: 'frequency_series' | 'time_frequency'; mode?: 'static' | 'dynamic'; quality?: { status: string; reasons: string[] } }
  channel_order: string[]
  requested_range: { start_s: number; end_s: number }
  actual_range: { start_s: number; end_s: number } | null
  axes: Record<string, number[]>
  axis_metadata: Record<string, { array_key: string; unit: string; length: number }>
  arrays: Record<string, unknown>
  array_metadata: Record<string, { unit: string; shape: number[] }>
  windows: Array<{ start_sample: number; end_sample: number; start_s: number; end_s: number; state: string; quality: string; failure?: { code: string; message: string } | null }>
  window_state_counts: Record<string, number>
  quality: string
  scientific_version: string
  implementation_version: string
}

export function listRunSummaries(recordingId: string): Promise<RunSummary[]> { return request(`/api/runs?recording_id=${encodeURIComponent(recordingId)}`) }
export function getResultView(runId: string): Promise<ResultView> { return request(`/api/runs/${encodeURIComponent(runId)}/result`) }
export function getStructuredPreview(runId: string, maxCells = 100_000): Promise<StructuredPreview> { return request(`/api/runs/${encodeURIComponent(runId)}/structured-preview?max_cells=${maxCells}`) }
export function exportUrl(runId: string, validationId?: string): string { const suffix = validationId ? `?validation_id=${encodeURIComponent(validationId)}` : ''; return `${apiBase}/api/runs/${encodeURIComponent(runId)}/export${suffix}` }
