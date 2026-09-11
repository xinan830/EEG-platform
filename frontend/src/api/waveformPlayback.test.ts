import { afterEach, describe, expect, it, vi } from 'vitest'
import { createWaveformPlayback, toWebSocketUrl } from './waveformPlayback'

afterEach(() => vi.unstubAllGlobals())

describe('toWebSocketUrl', () => {
  it('把后端 HTTP 地址和相对事件路径转换为可连接的 WebSocket 地址', () => {
    expect(toWebSocketUrl('http://127.0.0.1:8000', '/api/waveform-playback/abc/events'))
      .toBe('ws://127.0.0.1:8000/api/waveform-playback/abc/events')
  })

  it('创建播放会话时携带自定义 Montage', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const rows = [{ name: 'Left', terms: [{ channel: 'F3', weight: 1 }, { channel: 'Cz', weight: -1 }] }]
    await createWaveformPlayback('rec-1', ['F3', 'Cz'], 'custom_bipolar', [], rows)
    const body = JSON.parse(fetchMock.mock.calls[0][1].body)
    expect(body.custom_montage).toEqual(rows)
  })
})
