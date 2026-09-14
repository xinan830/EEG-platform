import { apiBase, request } from './client'

export interface RunSummary { run_id: string; recording_id: string; analysis_type: string; status: string; scientific_version: string; implementation_version: string; result_summary: Record<string, unknown> | null; error: { code: string; message: string } | null }
export interface ResultView { run: RunSummary; artifacts: Array<{ artifact_id: string; unit: string | null; shape: Record<string, number[]>; sha256: string }>; data_classification: { measured_data: boolean; algorithm_output: boolean; clinical_interpretation: boolean } }

export function listRunSummaries(recordingId: string): Promise<RunSummary[]> { return request(`/api/runs?recording_id=${encodeURIComponent(recordingId)}`) }
export function getResultView(runId: string): Promise<ResultView> { return request(`/api/runs/${encodeURIComponent(runId)}/result`) }
export function exportUrl(runId: string, validationId?: string): string { const suffix = validationId ? `?validation_id=${encodeURIComponent(validationId)}` : ''; return `${apiBase}/api/runs/${encodeURIComponent(runId)}/export${suffix}` }
