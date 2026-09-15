# Algorithm Catalog Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every algorithm-facing surface one shared, refreshable source for user definitions, official catalog entries and published definition versions.

**Architecture:** `App.vue` creates one `useAlgorithmCatalog()` context and passes it to the algorithm library and waveform-algorithm workspace. The context owns fetch/cache/refresh behavior, while each component retains its own selected item, modal state and unsaved form draft. Definition mutations call the context refresh operation after the backend confirms success.

**Tech Stack:** Vue 3 Composition API, TypeScript, Vitest, Vue Test Utils, existing FastAPI catalog endpoints.

**Spec:** `docs/superpowers/specs/2026-09-15-shared-workspace-state-design.md`

## Global Constraints

- Do not change EEG mathematics, units, analysis-time semantics or backend scientific ownership.
- Do not store EEG samples, PSD arrays or spectrogram matrices in the shared catalog.
- Official catalog failure must not hide user definitions; user-definition failure must not erase an already loaded official catalog.
- Preserve existing definition endpoints and user-algorithm runtime behavior.
- The catalog is not recording-scoped and must survive importing a different EEG file.
- All behavior changes use test-first development; full frontend Vitest, production build and `git diff --check` pass before commit.

---

### Task 1: Shared Algorithm Catalog Context

**Files:**
- Create: `frontend/src/composables/useAlgorithmCatalog.ts`
- Create: `frontend/src/composables/useAlgorithmCatalog.test.ts`
- Modify: `frontend/src/api/algorithmDefinitions.ts` only if a missing typed request prevents catalog loading

**Interfaces:**
- Consumes: `listDefinitions(): Promise<AlgorithmDefinition[]>`, `listDefinitionVersions(definitionId): Promise<AlgorithmDefinitionVersion[]>`, `listOfficialAlgorithms(): Promise<OfficialAlgorithmCatalogItem[]>`.
- Produces: `useAlgorithmCatalog(): AlgorithmCatalogContext` with `definitions`, `officialAlgorithms`, `versionsByDefinition`, `userError`, `officialError`, `loading`, `refresh()`, `ensureVersions(definitionId)`, and `removeDefinitionVersionCache(definitionId)`.

- [ ] **Step 1: Write the failing tests**

```ts
it('refreshes user and official catalogs independently', async () => {
  const catalog = useAlgorithmCatalog()
  await catalog.refresh()

  expect(catalog.definitions.value.map((item) => item.definition_id)).toEqual(['ratio'])
  expect(catalog.officialAlgorithms.value.map((item) => item.algorithm_id)).toEqual(['official-rbp'])
  expect(catalog.userError.value).toBe('')
  expect(catalog.officialError.value).toBe('')
})

it('preserves user definitions when the official catalog refresh fails', async () => {
  const catalog = useAlgorithmCatalog()
  await catalog.refresh()

  mockedListOfficialAlgorithms.mockRejectedValueOnce(new Error('catalog unavailable'))
  await catalog.refresh()

  expect(catalog.definitions.value.map((item) => item.definition_id)).toEqual(['ratio'])
  expect(catalog.officialError.value).toBe('官方算法目录暂不可读取；我的算法不受影响。')
})

it('caches versions by definition id and refreshes an explicit version request', async () => {
  const catalog = useAlgorithmCatalog()
  await catalog.ensureVersions('ratio')
  await catalog.ensureVersions('ratio')

  expect(mockedListDefinitionVersions).toHaveBeenCalledTimes(1)
  expect(catalog.versionsByDefinition.value.ratio[0].semver).toBe('1.0.0')
})
```

- [ ] **Step 2: Run the context tests to verify they fail**

Run: `npm.cmd test -- src/composables/useAlgorithmCatalog.test.ts`

Expected: FAIL because `useAlgorithmCatalog` does not exist.

- [ ] **Step 3: Implement the minimal catalog context**

```ts
export function useAlgorithmCatalog() {
  const definitions = ref<AlgorithmDefinition[]>([])
  const officialAlgorithms = ref<OfficialAlgorithmCatalogItem[]>([])
  const versionsByDefinition = ref<Record<string, AlgorithmDefinitionVersion[]>>({})
  const userError = ref('')
  const officialError = ref('')
  const loading = ref(false)

  async function refresh() {
    loading.value = true
    userError.value = ''
    officialError.value = ''
    const [user, official] = await Promise.allSettled([listDefinitions(), listOfficialAlgorithms()])
    if (user.status === 'fulfilled') definitions.value = user.value
    else userError.value = '我的算法目录暂不可读取。'
    if (official.status === 'fulfilled') officialAlgorithms.value = official.value
    else officialError.value = '官方算法目录暂不可读取；我的算法不受影响。'
    loading.value = false
  }

  async function ensureVersions(definitionId: string, force = false) {
    if (!force && versionsByDefinition.value[definitionId]) return versionsByDefinition.value[definitionId]
    const versions = await listDefinitionVersions(definitionId)
    versionsByDefinition.value = { ...versionsByDefinition.value, [definitionId]: versions }
    return versions
  }

  function removeDefinitionVersionCache(definitionId: string) {
    const { [definitionId]: removed, ...remaining } = versionsByDefinition.value
    void removed
    versionsByDefinition.value = remaining
  }
  return { definitions, officialAlgorithms, versionsByDefinition, userError, officialError, loading, refresh, ensureVersions, removeDefinitionVersionCache }
}
```

Do not use a shared singleton. `App.vue` must own the one context instance for a browser workspace.

- [ ] **Step 4: Run the context tests to verify they pass**

Run: `npm.cmd test -- src/composables/useAlgorithmCatalog.test.ts`

Expected: PASS with all three catalog behaviors green.

- [ ] **Step 5: Commit the isolated context**

```powershell
git add frontend/src/composables/useAlgorithmCatalog.ts frontend/src/composables/useAlgorithmCatalog.test.ts
git commit -m "Add shared algorithm catalog context"
```

### Task 2: Make the Waveform Algorithm Workspace Consume the Shared Catalog

**Files:**
- Modify: `frontend/src/components/AlgorithmDisplayWorkspace.vue`
- Modify: `frontend/src/components/AlgorithmDisplayWorkspace.test.ts`
- Modify: `frontend/src/App.vue`

**Interfaces:**
- Consumes: `catalog: AlgorithmCatalogContext` prop and `catalog.versionsByDefinition.value[definitionId]`.
- Produces: the existing algorithm checkbox and run flow, now based only on catalog state; removes `definitionsEpoch` and component-local definition/version loading.

- [ ] **Step 1: Write the failing component tests**

```ts
function createCatalogFixture() {
  return {
    definitions: ref([ratioDefinition]),
    officialAlgorithms: ref([officialRbp]),
    versionsByDefinition: ref({ ratio: [publishedRatioV1] }),
    userError: ref(''), officialError: ref(''), loading: ref(false),
    refresh: vi.fn(async () => undefined),
    ensureVersions: vi.fn(async (id: string) => id === 'ratio' ? [publishedRatioV1] : []),
    removeDefinitionVersionCache: vi.fn(),
  } as AlgorithmCatalogContext
}

it('renders user and official algorithms from the shared catalog without requesting a second list', async () => {
  const catalog = createCatalogFixture()
  const wrapper = mount(AlgorithmDisplayWorkspace, { props: { ...baseProps, catalog } })

  expect(wrapper.text()).toContain('Theta/Beta 比值')
  expect(wrapper.text()).toContain('相对频段功率')
  expect(mockedListDefinitions).not.toHaveBeenCalled()
})

it('uses the shared latest published version when it creates a Run', async () => {
  const catalog = createCatalogFixture()
  const wrapper = mount(AlgorithmDisplayWorkspace, { props: { ...baseProps, catalog } })
  await wrapper.get('input[type="checkbox"]').setValue(true)
  await wrapper.get('.primary-action').trigger('click')

  expect(createDefinitionMetricRun).toHaveBeenCalledWith(expect.objectContaining({ definitionVersion: '1.0.0' }))
})
```

- [ ] **Step 2: Run the workspace tests to verify they fail**

Run: `npm.cmd test -- src/components/AlgorithmDisplayWorkspace.test.ts`

Expected: FAIL because the component does not accept a `catalog` prop and still calls its own list APIs.

- [ ] **Step 3: Replace local catalog fetches with catalog consumption**

```ts
const props = defineProps<{
  recording: Recording; activeRange?: Range | null; rangeStart: number; rangeEnd: number;
  channels: string[]; playbackPositionS?: number; playing?: boolean; dynamicActive?: boolean;
  playbackEpoch?: number; catalog: AlgorithmCatalogContext
}>()
const definitions = computed(() => props.catalog.definitions.value)
const officialAlgorithms = computed(() => props.catalog.officialAlgorithms.value)
const versions = computed(() => props.catalog.versionsByDefinition.value)

onMounted(async () => {
  await props.catalog.refresh()
  await Promise.all(userDefinitions.value.map((item) => props.catalog.ensureVersions(item.definition_id)))
})
```

Keep `selectedIds`, dynamic-mode controls, calculation polling, request errors and chart display local to the workspace. Do not modify `createDefinitionMetricRun` payload semantics.

- [ ] **Step 4: Create the context in App and remove the manual epoch signal**

```ts
const algorithmCatalog = useAlgorithmCatalog()
void algorithmCatalog.refresh()

// Pass :catalog="algorithmCatalog" to AlgorithmDisplayWorkspace.
// Remove the definitionsEpoch prop from that component.
```

- [ ] **Step 5: Run the workspace tests to verify they pass**

Run: `npm.cmd test -- src/components/AlgorithmDisplayWorkspace.test.ts src/App.channelDialog.test.ts`

Expected: PASS; user algorithms remain selectable and official algorithms remain disabled.

- [ ] **Step 6: Commit the workspace migration**

```powershell
git add frontend/src/App.vue frontend/src/components/AlgorithmDisplayWorkspace.vue frontend/src/components/AlgorithmDisplayWorkspace.test.ts
git commit -m "Use shared algorithm catalog in workspace"
```

### Task 3: Make the Algorithm Library Refresh the Same Catalog After Mutations

**Files:**
- Modify: `frontend/src/components/AlgorithmDefinitionWorkbench.vue`
- Modify: `frontend/src/components/AlgorithmDefinitionWorkbench.test.ts`
- Modify: `frontend/src/App.vue`

**Interfaces:**
- Consumes: `catalog: AlgorithmCatalogContext` prop.
- Produces: definition create/save/publish/clone/delete call `catalog.refresh()` after their successful backend mutation; cached versions are updated or invalidated before the next selection.

- [ ] **Step 1: Write failing mutation-refresh tests**

```ts
it('refreshes the shared catalog after deleting a user algorithm', async () => {
  const catalog = createCatalogFixture()
  const wrapper = mount(AlgorithmDefinitionWorkbench, { props: { ...baseProps, catalog } })
  await wrapper.get('[aria-label="删除 Theta/Beta 比值"]').trigger('click')

  expect(catalog.refresh).toHaveBeenCalledTimes(1)
  expect(catalog.removeDefinitionVersionCache).toHaveBeenCalledWith('ratio')
})

it('refreshes the shared catalog after creating a definition version', async () => {
  const catalog = createCatalogFixture()
  const wrapper = mount(AlgorithmDefinitionWorkbench, { props: { ...baseProps, catalog } })
  await wrapper.get('.definition-mode').trigger('click')
  await wrapper.get('.definition-actions button:nth-child(2)').trigger('click')

  expect(catalog.refresh).toHaveBeenCalledTimes(1)
})
```

Use the existing backend API mocks. The first test must exercise the delete handler after `window.confirm` returns true; it must not merely assert a mocked emit.

- [ ] **Step 2: Run the library tests to verify they fail**

Run: `npm.cmd test -- src/components/AlgorithmDefinitionWorkbench.test.ts`

Expected: FAIL because the workbench holds a separate `definitions` list and does not receive a catalog prop.

- [ ] **Step 3: Refactor workbench reads and successful mutations**

```ts
const props = defineProps<{ recording: Recording; startS: number; endS: number; catalog: AlgorithmCatalogContext }>()
const definitions = computed(() => props.catalog.definitions.value)

async function afterDefinitionMutation(definitionId?: string) {
  if (definitionId) props.catalog.removeDefinitionVersionCache(definitionId)
  await props.catalog.refresh()
}
```

After `createDefinition`, `createDefinitionVersion`, `publishDefinitionVersion`, `cloneDefinition`, and `deleteDefinition`, call `afterDefinitionMutation`. Preserve the current selection when its id remains in the refreshed list; if deleted, select the first available definition or create a local new draft. Keep capabilities, draft JSON, developer mode and preview state component-local.

- [ ] **Step 4: Wire the shared catalog into App**

```ts
<AlgorithmDefinitionWorkbench
  :catalog="algorithmCatalog"
  :recording="recording"
  :start-s="activeAnalysisRange?.start ?? windowStartS"
  :end-s="activeAnalysisRange?.end ?? windowStartS + displaySettings.timebaseSeconds"
/>

<UserAlgorithmBuilder @saved="void algorithmCatalog.refresh()" />
```

Remove `algorithmDefinitionsEpoch`; do not force-close the algorithm workspace after a catalog refresh.

- [ ] **Step 5: Run the library tests to verify they pass**

Run: `npm.cmd test -- src/components/AlgorithmDefinitionWorkbench.test.ts src/components/AlgorithmDisplayWorkspace.test.ts src/App.channelDialog.test.ts`

Expected: PASS; create/delete mutations refresh both catalog consumers without a page reload.

- [ ] **Step 6: Commit the library migration**

```powershell
git add frontend/src/App.vue frontend/src/components/AlgorithmDefinitionWorkbench.vue frontend/src/components/AlgorithmDefinitionWorkbench.test.ts
git commit -m "Refresh shared catalog after definition mutations"
```

### Task 4: Final Verification and Documentation

**Files:**
- Modify: `docs/superpowers/specs/2026-09-15-shared-workspace-state-design.md` only if implementation reveals a behavior that differs from this approved design.

**Interfaces:**
- Consumes: completed catalog context and both catalog consumers.
- Produces: a clean catalog behavior with no `algorithmDefinitionsEpoch` symbol or component-local definition-list request in the two migrated components.

- [ ] **Step 1: Verify obsolete coordination is gone**

Run: `rg -n "algorithmDefinitionsEpoch|listDefinitions|listDefinitionVersions" frontend/src/App.vue frontend/src/components/AlgorithmDisplayWorkspace.vue frontend/src/components/AlgorithmDefinitionWorkbench.vue`

Expected: no `algorithmDefinitionsEpoch`; `listDefinitions` and `listDefinitionVersions` appear only in `useAlgorithmCatalog.ts` among the migrated catalog readers.

- [ ] **Step 2: Run the complete frontend test suite**

Run: `npm.cmd test`

Expected: every Vitest file passes with zero failing tests.

- [ ] **Step 3: Run the production type check and build**

Run: `npm.cmd run build`

Expected: `vue-tsc --noEmit` and Vite production build exit with code 0.

- [ ] **Step 4: Check the final diff**

Run: `git diff --check && git status --short`

Expected: no whitespace errors; only intended source, test and optional design-document changes remain.

- [ ] **Step 5: Commit verification-aligned documentation changes if needed**

```powershell
git add docs/superpowers/specs/2026-09-15-shared-workspace-state-design.md
git commit -m "Clarify shared algorithm catalog behavior"
```

Only create this commit if Task 4 changed the specification document.
