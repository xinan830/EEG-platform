import { ref, watch, type Ref } from 'vue'
import { getSpectrum } from '../api/spectrum'
import type { SpectrumResponse } from '../types/spectrum'
export function useSpectrum(recordingId: Ref<string | undefined>, startS: Ref<number>, channels: Ref<string[]>) {
  const result = ref<SpectrumResponse | null>(null); const loading = ref(false); const error = ref(''); let requestId = 0
  async function reload() {
    if (!recordingId.value || !channels.value.length) { result.value = null; return }
    const current = ++requestId; loading.value = true; error.value = ''
    try { const payload = await getSpectrum(recordingId.value, Math.max(0, startS.value), 30, channels.value); if (current === requestId) result.value = payload }
    catch (cause) { if (current === requestId) error.value = cause instanceof Error ? cause.message : '频谱读取失败' }
    finally { if (current === requestId) loading.value = false }
  }
  watch([recordingId, startS, channels], reload, { immediate: true })
  return { result, loading, error, reload }
}
