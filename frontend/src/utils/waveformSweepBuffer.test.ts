import { describe, expect, it } from 'vitest'
import { WaveformSweepBuffer } from './waveformSweepBuffer'

describe('WaveformSweepBuffer', () => {
  it('起始为空，chunk 只写到扫描指针且其后保留空白缺口', () => {
    const buffer = new WaveformSweepBuffer(10, ['Fz'], 1)

    buffer.push([[10], [20]])
    const frame = buffer.frame()

    expect([...frame.channels.Fz.slice(0, 2)]).toEqual([10, 20])
    expect(Number.isNaN(frame.channels.Fz[2])).toBe(true)
    expect(Number.isNaN(frame.channels.Fz[9])).toBe(true)
  })

  it('写满十秒扫屏后推进时间轴，并在新扫描头留下空白', () => {
    const buffer = new WaveformSweepBuffer(1, ['Fz'], 10)

    buffer.push([[1], [2], [3], [4], [5], [6], [7], [8], [9], [10]])
    const frame = buffer.frame()

    expect(frame.elapsed_s[0]).toBe(10)
    expect(Number.isNaN(frame.channels.Fz[0])).toBe(true)
    expect(frame.channels.Fz[1]).toBe(2)
  })

  it('二进制交错数据直接写入内部 TypedArray，不复制整屏历史数据', () => {
    const buffer = new WaveformSweepBuffer(10, ['Fz', 'Pz'], 1)
    buffer.pushInterleaved(new Float32Array([1, 10, 2, 20]))
    const first = buffer.frame()
    const second = buffer.frame()

    expect(first.channels.Fz).toBe(second.channels.Fz)
    expect([...first.channels.Fz.slice(0, 2)]).toEqual([1, 2])
    expect([...first.channels.Pz.slice(0, 2)]).toEqual([10, 20])
  })
})
