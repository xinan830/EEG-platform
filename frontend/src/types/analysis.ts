export interface IapfAttempt {
  elapsed_s: number
  iapf: number | null
  source: 'peak' | 'cog' | null
  signal_quality: number
  calibrated: boolean
  gate_failed: string | null
  peak_hz?: number | null
}

export interface MetricPoint {
  elapsed_s: number
  relaxation: number | null
  spatial_distribution: number | null
  rhythm_stability: number | null
  brainbeat: number | null
  fatigue: Record<string, number>
  signal_quality: number
  gate_failed: string | null
  iapf_hz: number
  iapf_is_fallback: boolean
  iapf_source: 'global_locked' | 'last_candidate' | 'default_10Hz'
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
  algorithm_contract?: { algorithm_version: string; [key: string]: unknown }
  faa?: { faa: number | null; reason: string; scope: string; [key: string]: unknown }
  analysis_input?: { sfreq_hz: number; sample_count: number; input_unit: string; mapped_channels: Record<string, string | null>; reference: string }
}

export interface AnalysisSummary {
  analysis_id: string
  recording_id: string
  status: 'completed'
  result_url: string
  locked_iapf: number | null
  algorithm_version: string | null
}
