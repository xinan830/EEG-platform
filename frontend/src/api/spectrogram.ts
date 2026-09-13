import type { SpectrogramResponse } from '../types/spectrogram'
const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://127.0.0.1:8000'
export async function getSpectrogram(recordingId: string, startS: number, windowS: number, channels: string[]): Promise<SpectrogramResponse> {
  const query = new URLSearchParams({ start_s: String(startS), window_s: String(windowS), channels: channels.join(',') })
  const response = await fetch(`${API_BASE}/api/recordings/${encodeURIComponent(recordingId)}/spectrogram?${query}`)
  if (!response.ok) throw new Error('无法读取时频图')
  return await response.json() as SpectrogramResponse
}

export async function getConfiguredSpectrogram(recordingId: string, config: Record<string, unknown>): Promise<SpectrogramResponse> {
  const response = await fetch(`${API_BASE}/api/recordings/${encodeURIComponent(recordingId)}/spectrogram/configured`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(config),
  })
  if (!response.ok) {
    let detail = ''
    try { const body = await response.json() as { detail?: string }; detail = body.detail ?? '' } catch { /* non-JSON error */ }
    throw new Error(detail ? `时频图读取失败：${detail}` : '无法读取时频图')
  }
  return await response.json() as SpectrogramResponse
}
