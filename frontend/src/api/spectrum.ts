import type { SpectrumResponse } from '../types/spectrum'
const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://127.0.0.1:8000'
export async function getSpectrum(recordingId: string, startS: number, windowS: number, channels: string[]): Promise<SpectrumResponse> {
  const query = new URLSearchParams({ start_s: String(startS), window_s: String(windowS), channels: channels.join(',') })
  const response = await fetch(`${API_BASE}/api/recordings/${encodeURIComponent(recordingId)}/spectrum?${query}`)
  if (!response.ok) {
    let detail = ''
    try { const body = await response.json() as { detail?: string }; detail = body.detail ?? '' } catch { /* non-JSON error */ }
    throw new Error(detail ? `频谱分析失败：${detail}` : '无法读取频谱分析结果')
  }
  return await response.json() as SpectrumResponse
}
export async function getConfiguredSpectrum(recordingId: string, config: Record<string, unknown>): Promise<SpectrumResponse> {
  const response = await fetch(`${API_BASE}/api/recordings/${encodeURIComponent(recordingId)}/spectrum/configured`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(config) })
  if (!response.ok) throw new Error('无法读取频谱分析结果')
  return await response.json() as SpectrumResponse
}
