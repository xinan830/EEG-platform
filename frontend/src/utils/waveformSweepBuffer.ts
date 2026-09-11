export interface WaveformSweepFrame {
  elapsed_s: Float64Array
  channels: Record<string, Float64Array>
  sweepStartS: number
  sweepPointer: number
}

/** 与桌面端 WaveformPreviewBuffer 对应的十秒扫描缓冲。 */
export class WaveformSweepBuffer {
  private readonly sampleCount: number
  private readonly gapSize: number
  private readonly values: Float64Array[]
  private readonly elapsedS: Float64Array
  private sweepStartS: number
  private sweepPointer = 0

  constructor(
    private readonly sfreq: number,
    private readonly channelNames: string[],
    private readonly windowSeconds = 10,
    startS = 0,
  ) {
    this.sampleCount = Math.max(1, Math.round(sfreq * windowSeconds))
    this.gapSize = Math.max(1, Math.floor(sfreq * 0.2))
    this.sweepStartS = startS
    this.values = channelNames.map(() => {
      const data = new Float64Array(this.sampleCount)
      data.fill(Number.NaN)
      return data
    })
    this.elapsedS = new Float64Array(this.sampleCount)
    this.updateElapsedTimes()
  }

  reset(startS = 0) {
    this.sweepStartS = startS
    this.sweepPointer = 0
    for (const channel of this.values) channel.fill(Number.NaN)
    this.updateElapsedTimes()
  }

  push(samples: number[][]) {
    if (!samples.length) return
    if (samples.some((row) => row.length !== this.channelNames.length)) {
      throw new Error('波形采样通道数与会话元数据不一致')
    }
    const newest = samples.length > this.sampleCount ? samples.slice(-this.sampleCount) : samples
    if (samples.length > this.sampleCount) {
      for (let sampleIndex = 0; sampleIndex < newest.length; sampleIndex += 1) {
        for (let channelIndex = 0; channelIndex < this.values.length; channelIndex += 1) {
          this.values[channelIndex][sampleIndex] = newest[sampleIndex][channelIndex]
        }
      }
      this.sweepPointer = 0
      this.sweepStartS += this.windowSeconds
      this.updateElapsedTimes()
    } else {
      for (const row of newest) {
        for (let channelIndex = 0; channelIndex < this.values.length; channelIndex += 1) {
          this.values[channelIndex][this.sweepPointer] = row[channelIndex]
        }
        this.sweepPointer += 1
        if (this.sweepPointer === this.sampleCount) {
          this.sweepPointer = 0
          this.sweepStartS += this.windowSeconds
          this.updateElapsedTimes()
        }
      }
    }
    this.clearSweepGap()
  }

  /** 直接写入 WebSocket 的交错 Float32 数据，避免构造二维普通数组。 */
  pushInterleaved(values: Float32Array) {
    if (values.length % this.channelNames.length !== 0) {
      throw new Error('二进制波形数据长度与会话通道数不一致')
    }
    const sampleCount = values.length / this.channelNames.length
    for (let sampleIndex = 0; sampleIndex < sampleCount; sampleIndex += 1) {
      const sourceOffset = sampleIndex * this.channelNames.length
      for (let channelIndex = 0; channelIndex < this.values.length; channelIndex += 1) {
        this.values[channelIndex][this.sweepPointer] = values[sourceOffset + channelIndex]
      }
      this.sweepPointer += 1
      if (this.sweepPointer === this.sampleCount) {
        this.sweepPointer = 0
        this.sweepStartS += this.windowSeconds
        this.updateElapsedTimes()
      }
    }
    this.clearSweepGap()
  }

  frame(): WaveformSweepFrame {
    return {
      // TypedArray 所有权归缓冲区；调用方只能读取，禁止在渲染层复制整屏波形。
      elapsed_s: this.elapsedS,
      channels: Object.fromEntries(this.channelNames.map((name, index) => [name, this.values[index]])),
      sweepStartS: this.sweepStartS,
      sweepPointer: this.sweepPointer,
    }
  }

  private clearSweepGap() {
    for (let offset = 0; offset < this.gapSize; offset += 1) {
      const index = (this.sweepPointer + offset) % this.sampleCount
      for (const channel of this.values) channel[index] = Number.NaN
    }
  }

  private updateElapsedTimes() {
    for (let index = 0; index < this.elapsedS.length; index += 1) {
      this.elapsedS[index] = this.sweepStartS + index / this.sfreq
    }
  }
}
