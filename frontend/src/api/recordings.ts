import type { Recording } from '../types/recording'
import { request } from './client'

export interface CustomMontageTerm {
  channel: string
  weight: number
}

export interface CustomMontageRow {
  name: string
  terms: CustomMontageTerm[]
}

export function listRecordings(): Promise<Recording[]> {
  return request<Recording[]>('/api/recordings')
}

export function importRecording(file: File): Promise<Recording> {
  const body = new FormData()
  body.append('file', file)
  return request<Recording>('/api/recordings/import', { method: 'POST', body })
}

export interface WaveformPreview {
  elapsed_s: number[]
  channels: Record<string, number[]>
  events: Array<{ elapsed_s: number; label: string }>
  duration_s: number
  window_start_s: number
  window_duration_s: number
  sfreq: number
  settings?: {
    low_cut_hz: number
    high_cut_hz: number
    notch_hz: number | null
    baseline_stabilization: boolean
    reference: string
    montage?: string
    average_exclude?: string[]
    custom_montage?: CustomMontageRow[]
    filter_contract?: Record<string, string | number | boolean>
  }
}

export interface WaveformWindowOptions {
  startS?: number
  windowS?: number
  lowCutHz?: number
  highCutHz?: number
  notchHz?: number | null
  baselineStabilization?: boolean
  reference?: string
  channels?: string[]
  montage?: string
  averageExclude?: string[]
  customMontage?: CustomMontageRow[]
}

export interface MontageOption {
  id: string
  label: string
  available: boolean
  channels: string[]
  missing: string[]
}

export interface EventMarker {
  id: string
  recording_id: string
  time_s: number
  label: string
  duration_s: number | null
  created_at: string
}

export function getEventMarkers(id: string): Promise<EventMarker[]> {
  return request(`/api/recordings/${id}/events`)
}

export function createEventMarker(id: string, payload: Pick<EventMarker, 'time_s' | 'label' | 'duration_s'>): Promise<EventMarker> {
  return request(`/api/recordings/${id}/events`, { method: 'POST', body: JSON.stringify(payload) })
}

export function deleteEventMarker(recordingId: string, markerId: string): Promise<void> {
  return request(`/api/recordings/${recordingId}/events/${markerId}`, { method: 'DELETE' })
}

export interface AlgorithmCheck {
  time_s: number
  montage: string
  montage_label: string
  average_participants: number
  formulas: Record<string, string>
  values_uv: Record<string, number | null>
  settings: {
    low_cut_hz: number
    high_cut_hz: number
    notch_hz: number | null
    baseline_stabilization: boolean
    montage: string
    average_exclude?: string[]
    filter_contract?: Record<string, string | number | boolean>
  }
}

export function getMontages(id: string): Promise<{ recording_id: string; montages: MontageOption[] }> {
  return request(`/api/recordings/${id}/montages`)
}

export function getAlgorithmCheck(id: string, options: WaveformWindowOptions & { timeS: number }): Promise<AlgorithmCheck> {
  const query = new URLSearchParams({
    time_s: String(options.timeS), low_cut_hz: String(options.lowCutHz ?? 0.5),
    high_cut_hz: String(options.highCutHz ?? 70), reference: options.reference ?? 'original',
  })
  if (options.notchHz != null) query.set('notch_hz', String(options.notchHz))
  query.set('baseline_stabilization', String(options.baselineStabilization ?? false))
  if (options.channels?.length) query.set('channels', options.channels.join(','))
  if (options.montage) query.set('montage', options.montage)
  if (options.averageExclude?.length) query.set('average_exclude', options.averageExclude.join(','))
  if (options.customMontage?.length) query.set('custom_montage', JSON.stringify(options.customMontage))
  return request(`/api/recordings/${id}/algorithm-check?${query}`)
}

export function getWaveformWindow(id: string, options: WaveformWindowOptions = {}): Promise<WaveformPreview> {
  const query = new URLSearchParams({
    start_s: String(options.startS ?? 0),
    window_s: String(options.windowS ?? 10),
    low_cut_hz: String(options.lowCutHz ?? 0.5),
    high_cut_hz: String(options.highCutHz ?? 70),
    reference: options.reference ?? 'original',
  })
  if (options.notchHz != null) query.set('notch_hz', String(options.notchHz))
  query.set('baseline_stabilization', String(options.baselineStabilization ?? false))
  if (options.channels?.length) query.set('channels', options.channels.join(','))
  if (options.montage) query.set('montage', options.montage)
  if (options.averageExclude?.length) query.set('average_exclude', options.averageExclude.join(','))
  if (options.customMontage?.length) query.set('custom_montage', JSON.stringify(options.customMontage))
  return request(`/api/recordings/${id}/window?${query}`)
}

/** 旧调用保持兼容，新的阅图流程使用 getWaveformWindow。 */
export function getPreview(id: string, startS = 0, windowS = 10): Promise<WaveformPreview> {
  return getWaveformWindow(id, { startS, windowS })
}
