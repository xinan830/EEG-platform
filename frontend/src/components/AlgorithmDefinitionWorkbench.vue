<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import type { AlgorithmCatalogContext } from '../composables/useAlgorithmCatalog'

const props = defineProps<{ catalog: AlgorithmCatalogContext }>()
const emit = defineEmits<{ close: [] }>()
const selectedId = ref<string | null>(null)
const official = computed(() => props.catalog.officialAlgorithms.value)
const selected = computed(() => official.value.find((item) => item.algorithm_id === selectedId.value) ?? official.value[0])

onMounted(async () => {
  await props.catalog.refresh()
  selectedId.value = official.value[0]?.algorithm_id ?? null
})
</script>

<template>
  <div class="modal-layer definition-modal-layer" @click.self="emit('close')">
    <section class="definition-workbench" aria-label="官方算法库">
      <header class="dialog-titlebar">
        <span class="app-glyph">◫</span><strong>官方算法库</strong>
        <button class="dialog-close" title="关闭" aria-label="关闭" @click="emit('close')">×</button>
      </header>
      <div class="definition-layout">
        <aside class="definition-sidebar">
          <p>官方算法</p>
          <button v-for="item in official" :key="item.algorithm_id" class="definition-list-item"
                  :class="{ selected: item.algorithm_id === selected?.algorithm_id }"
                  @click="selectedId = item.algorithm_id">
            <strong>{{ item.display_name_zh }}</strong><small>{{ item.abbreviation }}</small>
          </button>
        </aside>
        <main class="definition-editor">
          <section v-if="selected" class="algorithm-explainer">
            <p class="algorithm-explainer-kicker">官方算法 · {{ selected.is_runnable ? '可运行' : '暂不可运行' }}</p>
            <h2>{{ selected.display_name_zh }} · {{ selected.abbreviation }}</h2>
            <p class="algorithm-explainer-purpose">{{ selected.purpose_zh }}</p>
            <p>科学版本：{{ selected.scientific_version }}</p>
            <p>支持模式：{{ selected.supported_modes.map((mode) => mode === 'dynamic' ? '动态' : '静态').join('、') || '暂无' }}</p>
          </section>
          <p v-else class="definition-muted">{{ catalog.officialError.value || '暂无官方算法' }}</p>
        </main>
      </div>
    </section>
  </div>
</template>
