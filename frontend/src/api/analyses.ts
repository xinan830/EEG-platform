import type { AnalysisResult, AnalysisSummary } from '../types/analysis'
import { request } from './client'

export async function startAnalysis(recordingId: string): Promise<AnalysisResult> {
  const summary = await request<AnalysisSummary>(`/api/recordings/${recordingId}/analysis`, { method: 'POST' })
  return request<AnalysisResult>(summary.result_url)
}
