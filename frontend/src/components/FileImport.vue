<script setup lang="ts">
import { ref } from 'vue'
import { importRecording } from '../api/recordings'
import type { Recording } from '../types/recording'
import { isSupportedRecordingFile } from '../utils/recordingFile'
const emit = defineEmits<{ imported: [recording: Recording] }>()
const busy = ref(false); const error = ref('')
async function submit(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  error.value = ''
  if (!isSupportedRecordingFile(file.name)) {
    error.value = '请选择 BDF 或 EDF 格式的脑电文件。'
    input.value = ''
    return
  }
  busy.value = true
  try { emit('imported', await importRecording(file)) } catch (cause) { error.value = cause instanceof Error ? cause.message : '导入失败' } finally { busy.value = false; input.value = '' }
}
</script>
<template>
  <section class="startup-dialog" role="dialog" aria-modal="true" aria-labelledby="startup-title">
    <div class="dialog-titlebar"><span class="app-glyph">▣</span><span id="startup-title">打开脑电文件</span></div>
    <div class="dialog-body">
      <p class="import-description">选择本地 BDF 或 EDF 文件，系统会直接读取并展示原始 EEG 波形。</p>
      <label class="file-button import-file-button"><input type="file" accept=".bdf,.edf" :disabled="busy" @change="submit" />{{ busy ? '正在读取文件…' : '选择 BDF/EDF 文件…' }}</label>
      <p class="import-note">支持 .bdf、.edf 格式</p>
      <p v-if="error" class="error">{{ error }}</p>
    </div>
  </section>
</template>
