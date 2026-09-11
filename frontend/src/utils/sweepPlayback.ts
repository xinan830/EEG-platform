export interface SweepAdvance {
  visibleSamples: number
  completed: boolean
}

/**
 * 复刻桌面端示波器的扫描指针：一帧只写入新到达的一小块采样，
 * 未经过指针的位置保持空白，而不是把缓存的整个窗口立即绘制出来。
 */
export function advanceSweep(visibleSamples: number, samplesPerTick: number, totalSamples: number): SweepAdvance {
  const visible = Math.max(0, Math.min(visibleSamples, totalSamples))
  const chunk = Math.max(1, Math.floor(samplesPerTick))
  const next = Math.min(totalSamples, visible + chunk)
  return { visibleSamples: next, completed: next >= totalSamples }
}
