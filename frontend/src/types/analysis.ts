export interface IapfAttempt {
  elapsed_s: number
  iapf: number | null
  source: 'peak' | 'cog' | null
  signal_quality: number
  calibrated: boolean
  gate_failed: string | null
}

export interface MetricPoint {
  elapsed_s: number
  relaxation: number
  spatial_distribution: number
  rhythm_stability: number | null
  brainbeat: number | null
  fatigue: Record<string, number>
}

export interface AnalysisResult {
  analysis_id: string
  recording_id: string
  status: 'completed'
  duration_s: number
  locked_iapf: number | null
  iapf_attempts: IapfAttempt[]
  metrics: MetricPoint[]
  events: Array<{ elapsed_s: number; label: string }>
  waveform: { elapsed_s: number[]; channels: Record<string, number[]> }
}

export interface AnalysisSummary {
  analysis_id: string
  recording_id: string
  status: 'completed'
  result_url: string
  locked_iapf: number | null
}
