import { describe, expect, it } from 'vitest'
import { clampPageViewportStart, moveViewportByPixels, pagedViewportStart, playbackPageStart, zoomViewport } from './waveformViewport'

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

describe('playbackPageStart', () => {
  it('页内保持固定，到边界后切换到下一页', () => {
    expect(playbackPageStart(7.3, 10)).toBe(0)
    expect(playbackPageStart(9.99, 10)).toBe(0)
    expect(playbackPageStart(10, 10)).toBe(10)
    expect(playbackPageStart(18.4, 10)).toBe(10)
    expect(playbackPageStart(20, 10)).toBe(20)
  })

  it('最后一页不足一屏时仍保持固定分页起点', () => {
    expect(playbackPageStart(94.9, 10, 95)).toBe(90)
    expect(playbackPageStart(95, 10, 95)).toBe(90)
  })
})

describe('clampPageViewportStart', () => {
  it('缩放视窗只能在当前时间基页面内部移动', () => {
    expect(clampPageViewportStart(-2, 4, 0, 10)).toBe(0)
    expect(clampPageViewportStart(3, 4, 0, 10)).toBe(3)
    expect(clampPageViewportStart(8, 4, 0, 10)).toBe(6)
    expect(clampPageViewportStart(18, 4, 10, 10)).toBe(16)
  })

  it('可见时长等于时间基时不允许页内平移', () => {
    expect(clampPageViewportStart(5, 10, 0, 10)).toBe(0)
  })
})

describe('pagedViewportStart', () => {
  it('始终按固定屏幕边界翻页', () => {
    expect(pagedViewportStart(0, 1, 10, 100)).toBe(10)
    expect(pagedViewportStart(10, 1, 10, 100)).toBe(20)
    expect(pagedViewportStart(20, -1, 10, 100)).toBe(10)
    expect(pagedViewportStart(5.4, 1, 10, 100)).toBe(10)
    expect(pagedViewportStart(5.4, -1, 10, 100)).toBe(0)
    expect(pagedViewportStart(85, -1, 10, 95)).toBe(80)
    expect(pagedViewportStart(85, 1, 10, 95)).toBe(90)
  })

  it('在记录首尾处截断', () => {
    expect(pagedViewportStart(0, -1, 10, 95)).toBe(0)
    expect(pagedViewportStart(80, 1, 10, 95)).toBe(90)
    expect(pagedViewportStart(90, 1, 10, 95)).toBe(90)
  })
})
