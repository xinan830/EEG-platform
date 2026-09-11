import { describe, expect, it } from 'vitest'
import { moveViewportByPixels, zoomViewport } from './waveformViewport'

describe('zoomViewport', () => {
  it('缩放时保持鼠标下的时间点不变', () => {
    const viewport = zoomViewport({
      start: 20,
      duration: 40,
      total: 120,
      anchorRatio: 0.25,
      deltaY: -1,
    })

    expect(viewport.duration).toBeCloseTo(32.8)
    expect(viewport.start + viewport.duration * 0.25).toBeCloseTo(30)
  })

  it('拖动时间轴时不会越过数据的开始或结束', () => {
    expect(moveViewportByPixels({ start: 20, duration: 30, total: 100, deltaX: 400, width: 800 }).start).toBe(5)
    expect(moveViewportByPixels({ start: 4, duration: 30, total: 100, deltaX: 1000, width: 800 }).start).toBe(0)
    expect(moveViewportByPixels({ start: 65, duration: 30, total: 100, deltaX: -1000, width: 800 }).start).toBe(70)
  })
})
