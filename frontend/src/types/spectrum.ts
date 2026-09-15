import type { AnalysisProvenance } from './analysisProvenance'

export type SpectrumBand = 'delta' | 'theta' | 'alpha' | 'beta'
export interface SpectrumExecutionConfig {
  mode: 'static' | 'dynamic'; channels: string[]; dynamic_window_s: number; refresh_step_s: number
  requested_time: { start_s: number; end_s: number }
  actual_time: { start_s: number; end_s: number; duration_s: number }
  preprocessing: Record<string, unknown>; welch: Record<string, unknown>
}
export interface SpectrumResponse {
  recording_id: string; window_start_s: number; window_duration_s: number; sfreq_hz: number
  channels: string[]; analysis_reference: string; algorithm_version: string
  units: { psd: string; absolute_power: string; relative_power: string }
  frequencies_hz: number[]; psd: Record<string, number[]>
  band_power: Record<string, Record<SpectrumBand, number>>
  relative_band_power: Record<string, Record<SpectrumBand, number>>
  quality: { clean_segments: number; total_segments: number; clean_ratio: number; gate_failed: string | null }
  baseline_algorithm_version?: string; requested_config?: Record<string, unknown>; execution_config?: SpectrumExecutionConfig; analysis_config_hash?: string; warmup?: boolean
  requested_start_s?: number; requested_end_s?: number; requested_window_s?: number; actual_start_s?: number; actual_end_s?: number; actual_duration_s?: number
  analysis_provenance?: AnalysisProvenance
}
