// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { afterEach, expect, it, vi } from 'vitest'
import ChannelMapping from './ChannelMapping.vue'
import { saveMapping } from '../api/recordings'

vi.mock('../api/recordings', () => ({ saveMapping: vi.fn() }))

afterEach(() => vi.clearAllMocks())

const recording = {
  id: 'recording-1', channels: ['Fz', 'Pz', 'O2', 'F3', 'F4'], mapping: null,
}

it('suggests O2 for an absent Oz but saves it only after explicit confirmation', async () => {
  vi.mocked(saveMapping).mockResolvedValue({ ...recording, mapping: { fz: 'Fz', pz: 'Pz', oz: 'O2', f3: 'F3', f4: 'F4' } } as never)
  const wrapper = mount(ChannelMapping, { props: { recording } as never })

  expect(wrapper.text()).toContain('已建议将 O2 用作 Oz')
  const selects = wrapper.findAll('select')
  expect((selects[2].element as HTMLSelectElement).value).toBe('O2')
  expect(saveMapping).not.toHaveBeenCalled()

  await wrapper.get('.channel-confirm').trigger('click')

  expect(saveMapping).toHaveBeenCalledWith('recording-1', { fz: 'Fz', pz: 'Pz', oz: 'O2', f3: 'F3', f4: 'F4' })
  expect(wrapper.emitted('saved')?.[0]).toEqual([{ ...recording, mapping: { fz: 'Fz', pz: 'Pz', oz: 'O2', f3: 'F3', f4: 'F4' } }])
})
