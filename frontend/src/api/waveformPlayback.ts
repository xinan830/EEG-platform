import { request } from './client'

const apiBase = import.meta.env.VITE_API_BASE ?? 'http://127.0.0.1:8000'

export interface WaveformPlaybackSession {
  session_id: string
  recording_id: string
  status: string
  websocket_url: string
}

export function createWaveformPlayback(recordingId: string, channels: string[], montage = 'original', averageExclude: string[] = []): Promise<WaveformPlaybackSession> {
  return request(`/api/recordings/${recordingId}/waveform-playback`, {
    method: 'POST',
    body: JSON.stringify({ channels, montage, average_exclude: averageExclude }),
  })
}

export function controlWaveformPlayback(
  sessionId: string,
  action: 'pause' | 'resume' | 'restart' | 'seek' | 'set_filters' | 'set_channels' | 'stop',
  payload: Record<string, number | string[] | boolean | null> = {},
) {
  return request(`/api/waveform-playback/${sessionId}/control`, {
    method: 'POST',
    body: JSON.stringify({ action, ...payload }),
  })
}

export function toWebSocketUrl(base: string, path: string): string {
  const url = new URL(path, base)
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:'
  return url.toString()
}

export function waveformPlaybackSocketUrl(path: string): string {
  return toWebSocketUrl(apiBase, path)
}
