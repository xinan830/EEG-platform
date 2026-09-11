import { describe, expect, it } from 'vitest'
import { toWebSocketUrl } from './waveformPlayback'

describe('toWebSocketUrl', () => {
  it('把后端 HTTP 地址和相对事件路径转换为可连接的 WebSocket 地址', () => {
    expect(toWebSocketUrl('http://127.0.0.1:8000', '/api/waveform-playback/abc/events'))
      .toBe('ws://127.0.0.1:8000/api/waveform-playback/abc/events')
  })
})
