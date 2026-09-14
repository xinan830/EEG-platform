// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App.vue'
import FileImport from './components/FileImport.vue'
import ViewerToolbar from './components/ViewerToolbar.vue'
import { getMontages, getWaveformWindow, type WaveformPreview } from './api/recordings'
import type { Recording } from './types/recording'

vi.mock('./api/recordings', () => ({
  importRecording: vi.fn(),
  listRecordings: vi.fn(),
  saveMapping: vi.fn(),
  getWaveformWindow: vi.fn(),
  getMontages: vi.fn(),
  getPreview: vi.fn(),
  getEventMarkers: vi.fn().mockResolvedValue([]),
  createEventMarker: vi.fn(),
  deleteEventMarker: vi.fn(),
}))

vi.mock('./api/waveformPlayback', () => ({
  createWaveformPlayback: vi.fn(),
  controlWaveformPlayback: vi.fn(),
  toWebSocketUrl: (base: string, path: string) => `ws://test${path}`,
  waveformPlaybackSocketUrl: (path: string) => `ws://test${path}`,
}))

const fakeRecording: Recording = {
  id: 'rec-1',
  original_name: 'demo.bdf',
  stored_name: 'demo.bdf',
  extension: '.bdf',
  created_at: '2026-09-10T00:00:00Z',
  sfreq: 512,
  duration_s: 60,
  channels: ['Fp1', 'Fp2', 'F3', 'Fz', 'Pz', 'O1', 'O2'],
  mapping: null,
}

const fakeWindow: WaveformPreview = {
  elapsed_s: [0, 1, 2],
  channels: { Fz: [1, 2, 3], Pz: [1, 2, 3], O1: [1, 2, 3] },
  events: [],
  duration_s: 60,
  window_start_s: 0,
  window_duration_s: 10,
  sfreq: 512,
}

/** WaveformPanel 依赖 canvas 与 ResizeObserver，jsdom 中用桩替换，不影响弹窗接线。 */
async function mountImportedApp() {
  vi.mocked(getWaveformWindow).mockResolvedValue(fakeWindow)
  vi.mocked(getMontages).mockResolvedValue({
    recording_id: fakeRecording.id,
    montages: [
      { id: 'original', label: '原始记录（不重参考）', available: true, channels: fakeRecording.channels, missing: [] },
      { id: 'custom_bipolar', label: '自定义 Montage', available: true, channels: [], missing: [] },
    ],
  })
  const wrapper = mount(App, { global: { stubs: { WaveformPanel: true } } })
  await wrapper.findComponent(FileImport).vm.$emit('imported', fakeRecording)
  await flushPromises()
  return wrapper
}

function lastWindowOptions() {
  return vi.mocked(getWaveformWindow).mock.calls.at(-1)?.[1]
}

function findFooterButton(wrapper: Awaited<ReturnType<typeof mountImportedApp>>, text: string) {
  const button = wrapper.findAll('.channel-dialog-footer button').find((item) => item.text() === text)
  expect(button).toBeDefined()
  return button!
}

describe('导入后的通道选择流程', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('导入完成后自动弹出通道设置，确认前不读取波形', async () => {
    const wrapper = await mountImportedApp()
    expect(wrapper.find('.channel-dialog').exists()).toBe(true)
    expect(getWaveformWindow).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('默认展示科研工作流而不展示开发者诊断，开发者模式按需打开', async () => {
    const wrapper = await mountImportedApp()
    await findFooterButton(wrapper, '取消').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('1 波形查看')
    expect(wrapper.text()).toContain('2 频谱分析')
    expect(wrapper.text()).toContain('3 时频分析')
    expect(wrapper.find('.debug-console').exists()).toBe(false)
    expect(wrapper.text()).toContain('仅影响当前波形显示，不改变频谱或时频分析算法')

    await wrapper.get('.app-mode-toggle').trigger('click')
    expect(wrapper.find('.debug-console').exists()).toBe(true)
    wrapper.unmount()
  })

  it('首次弹窗取消后按默认通道从 0 秒进入阅图；此后工具栏打开的弹窗取消只关闭', async () => {
    const wrapper = await mountImportedApp()
    await findFooterButton(wrapper, '取消').trigger('click')
    await flushPromises()
    expect(wrapper.find('.channel-dialog').exists()).toBe(false)
    expect(getWaveformWindow).toHaveBeenCalledTimes(1)
    expect(lastWindowOptions()?.startS).toBe(0)
    // chooseWaveformChannels 偏好匹配 Fz/Pz/O2/F3 后，再从剩余通道补足到 5 个（Fp1）。
    expect(lastWindowOptions()?.channels).toEqual(['Fz', 'Pz', 'O2', 'F3', 'Fp1'])

    await wrapper.findComponent(ViewerToolbar).vm.$emit('channels')
    await wrapper.find('.dialog-close').trigger('click')
    expect(wrapper.find('.channel-dialog').exists()).toBe(false)

    await wrapper.findComponent(ViewerToolbar).vm.$emit('channels')
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await flushPromises()
    expect(wrapper.find('.channel-dialog').exists()).toBe(false)
    expect(getWaveformWindow).toHaveBeenCalledTimes(1)
    wrapper.unmount()
  })

  it('首次弹窗确认后按所选通道从 0 秒重读，导入阶段只读取一次', async () => {
    const wrapper = await mountImportedApp()
    const option = (name: string) =>
      wrapper.findAll('.channel-option').find((item) => item.find('span').text() === name)
    await option('O1')!.find('input').trigger('change')
    await option('F3')!.find('input').trigger('change')
    await option('Fp1')!.find('input').trigger('change')
    await findFooterButton(wrapper, '应用并从头显示').trigger('click')
    await flushPromises()

    expect(wrapper.find('.channel-dialog').exists()).toBe(false)
    expect(getWaveformWindow).toHaveBeenCalledTimes(1)
    expect(lastWindowOptions()?.startS).toBe(0)
    expect(lastWindowOptions()?.channels).toEqual(['Fz', 'Pz', 'O1', 'O2'])
    wrapper.unmount()
  })

  it('重新导入第二个文件后会重置阅图状态，不需要刷新页面', async () => {
    const wrapper = await mountImportedApp()
    await findFooterButton(wrapper, '取消').trigger('click')
    await flushPromises()
    await wrapper.findComponent(ViewerToolbar).vm.$emit('open')
    await flushPromises()
    expect(wrapper.findComponent(FileImport).exists()).toBe(true)

    const second: Recording = { ...fakeRecording, id: 'rec-2', original_name: 'second.edf', extension: '.edf' }
    await wrapper.findComponent(FileImport).vm.$emit('imported', second)
    await flushPromises()
    expect(wrapper.find('.header-file').text()).toContain('second.edf')
    expect(wrapper.find('.channel-dialog').exists()).toBe(true)
    await findFooterButton(wrapper, '取消').trigger('click')
    await flushPromises()
    expect(lastWindowOptions()?.startS).toBe(0)
    wrapper.unmount()
  })

  it('下一屏按当前时基前进，并将新屏起点设为播放位置', async () => {
    const wrapper = await mountImportedApp()
    await findFooterButton(wrapper, '取消').trigger('click')
    await flushPromises()
    vi.mocked(getWaveformWindow).mockImplementation(async (_id, options) => {
      const startS = options?.startS ?? 0
      return { ...fakeWindow, elapsed_s: [startS, startS + 1], window_start_s: startS }
    })

    await wrapper.findComponent(ViewerToolbar).vm.$emit('nextScreen')
    await flushPromises()

    expect(lastWindowOptions()?.startS).toBe(10)
    expect(wrapper.find('.file-summary').text()).toContain('10.0 / 60')
    wrapper.unmount()
  })

  it('自定义 Montage 应用后从头读取并传递明确的正负极定义', async () => {
    const wrapper = await mountImportedApp()
    await findFooterButton(wrapper, '取消').trigger('click')
    await flushPromises()
    await wrapper.find('.montage-selector select').setValue('custom_bipolar')
    await flushPromises()

    expect(wrapper.find('.custom-montage-dialog').exists()).toBe(true)
    await wrapper.find('.custom-montage-name input').setValue('F3-Fz')
    const selects = wrapper.findAll('.custom-montage-term select')
    await selects[0].setValue('F3')
    await selects[1].setValue('Fz')
    await wrapper.find('.custom-montage-dialog .channel-confirm').trigger('click')
    await flushPromises()

    expect(wrapper.find('.custom-montage-dialog').exists()).toBe(false)
    expect(lastWindowOptions()).toMatchObject({
      startS: 0,
      montage: 'custom_bipolar',
      customMontage: [{ name: 'F3-Fz', terms: [{ channel: 'F3', weight: 1 }, { channel: 'Fz', weight: -1 }] }],
    })
    wrapper.unmount()
  })
})
