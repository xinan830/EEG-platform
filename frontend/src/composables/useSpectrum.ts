import { ref, watch, type Ref } from 'vue'
import { getConfiguredSpectrum } from '../api/spectrum'
import type { SpectrumResponse } from '../types/spectrum'
export function useSpectrum(recordingId: Ref<string | undefined>, startS: Ref<number>, windowS: Ref<number>, channels: Ref<string[]>, mode: Ref<'static' | 'dynamic'> = ref('static'), refreshStepS: Ref<number> = ref(1), requestedDynamicWindowS: Ref<number> = windowS) {
  const result = ref<SpectrumResponse | null>(null); const loading = ref(false); const error = ref(''); let requestId = 0; let queued = false
  async function reload() {
    if (!recordingId.value || !channels.value.length) { result.value = null; return }
    if (windowS.value < 4) return
    if (loading.value) { queued = true; return }
    const current = ++requestId; loading.value = true; error.value = ''
    const start = Math.max(0, startS.value); const end = start + windowS.value
    try { const payload = await getConfiguredSpectrum(recordingId.value, { mode: mode.value, channels: channels.value, time: { start_s: start, end_s: end }, dynamic_window_s: mode.value === 'dynamic' ? requestedDynamicWindowS.value : 10, refresh_step_s: refreshStepS.value }); if (current === requestId) result.value = payload }
    catch (cause) { if (current === requestId) error.value = cause instanceof Error ? cause.message : '频谱读取失败' }
    finally {
      if (current === requestId) loading.value = false
      if (queued && current === requestId) { queued = false; void reload() }
    }
  }
  // Callers control the refresh cadence: static mode changes startS per screen;
  // dynamic mode supplies a once-per-second rolling start and avoids per-frame requests.
  watch([recordingId, startS, windowS, channels, mode, refreshStepS, requestedDynamicWindowS], reload, { immediate: true })
  return { result, loading, error, reload }
}
