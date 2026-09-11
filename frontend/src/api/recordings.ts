import type { ChannelMapping, Recording } from '../types/recording'
import { request } from './client'

export function listRecordings(): Promise<Recording[]> {
  return request<Recording[]>('/api/recordings')
}

export function importRecording(file: File): Promise<Recording> {
  const body = new FormData()
  body.append('file', file)
  return request<Recording>('/api/recordings/import', { method: 'POST', body })
}

export function saveMapping(id: string, mapping: ChannelMapping): Promise<Recording> {
  return request<Recording>(`/api/recordings/${id}/mapping`, { method: 'PUT', body: JSON.stringify(mapping) })
}

export interface WaveformPreview {
  elapsed_s: number[]
  channels: Record<string, number[]>
  events: Array<{ elapsed_s: number; label: string }>
  duration_s: number
  window_start_s: number
  window_duration_s: number
  sfreq: number
  settings?: { low_cut_hz: number; high_cut_hz: number; notch_hz: number | null; reference: string }
}

export interface WaveformWindowOptions {
  startS?: number
  windowS?: number
  lowCutHz?: number
  highCutHz?: number
  notchHz?: number | null
  reference?: string
  channels?: string[]
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
  if (options.channels?.length) query.set('channels', options.channels.join(','))
  return request(`/api/recordings/${id}/window?${query}`)
}

/** 旧调用保持兼容，新的阅图流程使用 getWaveformWindow。 */
export function getPreview(id: string, startS = 0, windowS = 10): Promise<WaveformPreview> {
  return getWaveformWindow(id, { startS, windowS })
}
