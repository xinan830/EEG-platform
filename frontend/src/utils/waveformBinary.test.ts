import { describe, expect, it } from 'vitest'
import { decodeWaveformBinary } from './waveformBinary'

describe('decodeWaveformBinary', () => {
  it('解析文件绝对时间和按样本交错排列的 float32 微伏数据', () => {
    const buffer = new ArrayBuffer(8 + 4 * 6)
    new DataView(buffer).setFloat64(0, 1.25, true)
    new Float32Array(buffer, 8).set([1, 2, 3, 4, 5, 6])

    expect(decodeWaveformBinary(buffer, 3)).toMatchObject({ elapsedS: 1.25 })
    expect([...decodeWaveformBinary(buffer, 3).values]).toEqual([1, 2, 3, 4, 5, 6])
  })

  it('拒绝与会话通道数不一致的数据帧', () => {
    const buffer = new ArrayBuffer(8 + 4 * 5)
    expect(() => decodeWaveformBinary(buffer, 3)).toThrow('通道数')
  })
})
