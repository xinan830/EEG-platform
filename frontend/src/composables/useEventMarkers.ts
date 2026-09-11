import { ref, watch, type Ref } from 'vue'
import { createEventMarker, deleteEventMarker, getEventMarkers, type EventMarker } from '../api/recordings'
import type { Recording } from '../types/recording'

export function useEventMarkers(recording: Ref<Recording | null>, error: Ref<string>) {
  const markers = ref<EventMarker[]>([])

  async function load() {
    markers.value = recording.value ? await getEventMarkers(recording.value.id) : []
  }

  async function create(timeS: number, label: string, durationS: number | null = null) {
    if (!recording.value || !label.trim()) return
    try {
      await createEventMarker(recording.value.id, { time_s: timeS, label: label.trim(), duration_s: durationS })
      await load()
    } catch (reason) {
      error.value = reason instanceof Error ? reason.message : '创建事件失败'
    }
  }

  async function remove(markerId: string) {
    if (!recording.value) return
    try {
      await deleteEventMarker(recording.value.id, markerId)
      markers.value = markers.value.filter((marker) => marker.id !== markerId)
    } catch (reason) {
      error.value = reason instanceof Error ? reason.message : '删除事件失败'
    }
  }

  watch(recording, () => { void load() }, { immediate: true })
  return { markers, create, remove }
}
