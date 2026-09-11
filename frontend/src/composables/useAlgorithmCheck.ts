import { ref, type Ref } from 'vue'
import { getAlgorithmCheck, type AlgorithmCheck, type CustomMontageRow } from '../api/recordings'
import type { Recording } from '../types/recording'
import type { DisplaySettings } from '../utils/displaySettings'

export function useAlgorithmCheck(
  recording: Ref<Recording | null>, settings: Ref<DisplaySettings>, channels: Ref<string[]>, montage: Ref<string>, averageExclude: Ref<string[]>, customMontage: Ref<CustomMontageRow[]>, error: Ref<string>,
) {
  const open = ref(false)
  const loading = ref(false)
  const seconds = ref(0)
  const result = ref<AlgorithmCheck | null>(null)

  async function inspect(timeS = seconds.value) {
    const current = recording.value
    if (!current || !Number.isFinite(timeS) || timeS < 0) { error.value = '检验时间必须是大于等于 0 的秒数'; return }
    seconds.value = timeS
    loading.value = true
    try {
      result.value = await getAlgorithmCheck(current.id, {
        timeS, lowCutHz: settings.value.lowCutHz, highCutHz: settings.value.highCutHz,
        notchHz: settings.value.notchHz, baselineStabilization: settings.value.baselineStabilization, reference: settings.value.reference,
        montage: montage.value, channels: channels.value,
        averageExclude: averageExclude.value,
        customMontage: customMontage.value,
      })
    } catch (cause) { error.value = cause instanceof Error ? cause.message : '无法执行算法检验' }
    finally { loading.value = false }
  }

  async function show() { open.value = true; await inspect() }
  function close() { open.value = false }
  function reset() { result.value = null; seconds.value = 0; open.value = false }
  return { open, loading, seconds, result, show, close, inspect, reset }
}
