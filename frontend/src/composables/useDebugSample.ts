import { ref, type Ref } from 'vue'
import { getWaveformWindow, type CustomMontageRow } from '../api/recordings'
import type { Recording } from '../types/recording'
import type { DisplaySettings } from '../utils/displaySettings'

export type DebugSample = { time: number; values: Record<string, number> }

export function useDebugSample(
  recording: Ref<Recording | null>,
  totalDurationS: Ref<number | undefined>,
  settings: Ref<DisplaySettings>,
  channelNames: Ref<string[]>,
  montage: Ref<string>,
  customMontage: Ref<CustomMontageRow[]>,
  error: Ref<string>,
) {
  const seconds = ref(0)
  const loading = ref(false)
  const sample = ref<DebugSample | null>(null)

  async function inspect() {
    const current = recording.value
    const absoluteSeconds = Number(seconds.value)
    if (!current) return
    if (!Number.isFinite(absoluteSeconds) || absoluteSeconds < 0) {
      error.value = '调试时间必须是大于等于 0 的秒数'
      return
    }
    if (totalDurationS.value !== undefined && absoluteSeconds > totalDurationS.value) {
      error.value = '调试时间超出当前文件范围'
      return
    }
    loading.value = true
    error.value = ''
    try {
      const payload = await getWaveformWindow(current.id, {
        startS: absoluteSeconds,
        windowS: 0.1,
        lowCutHz: settings.value.lowCutHz,
        highCutHz: settings.value.highCutHz,
        notchHz: settings.value.notchHz,
        baselineStabilization: settings.value.baselineStabilization,
        reference: settings.value.reference,
        channels: channelNames.value,
        montage: montage.value,
        customMontage: customMontage.value,
      })
      const values = Object.fromEntries(Object.entries(payload.channels).map(([name, values]) => [name, values[0] ?? Number.NaN]))
      sample.value = { time: payload.elapsed_s[0] ?? absoluteSeconds, values }
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '无法读取调试采样点'
    } finally {
      loading.value = false
    }
  }

  function reset() {
    seconds.value = 0
    sample.value = null
  }

  return { seconds, loading, sample, inspect, reset }
}
