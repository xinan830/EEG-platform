import { describe, expect, it } from 'vitest'
import { advanceSweep } from './sweepPlayback'

describe('advanceSweep', () => {
  it('一次播放 tick 只能推进一个小采样块，不能把整个十秒窗口一次显示出来', () => {
    expect(advanceSweep(0, 25, 5000)).toEqual({ visibleSamples: 25, completed: false })
  })

  it('到达当前窗口末尾时才标记完成', () => {
    expect(advanceSweep(4_980, 25, 5_000)).toEqual({ visibleSamples: 5_000, completed: true })
  })
})
