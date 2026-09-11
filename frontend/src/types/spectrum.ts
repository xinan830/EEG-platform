export type SpectrumBand = 'delta' | 'theta' | 'alpha' | 'beta'
export interface SpectrumResponse {
  recording_id: string; window_start_s: number; window_duration_s: number; sfreq_hz: number
  channels: string[]; analysis_reference: string; algorithm_version: string
  units: { psd: string; absolute_power: string; relative_power: string }
  frequencies_hz: number[]; psd: Record<string, number[]>
  band_power: Record<string, Record<SpectrumBand, number>>
  relative_band_power: Record<string, Record<SpectrumBand, number>>
  quality: { clean_segments: number; total_segments: number; clean_ratio: number; gate_failed: string | null }
}
