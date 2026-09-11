// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import CustomMontageDialog from './CustomMontageDialog.vue'

const channels = ['F3', 'Fz', 'F4', 'Cz']

describe('CustomMontageDialog', () => {
  it('提交明确的正负极定义，不根据选择顺序猜测', async () => {
    const wrapper = mount(CustomMontageDialog, { props: { channels, rows: [] } })
    await wrapper.find('input').setValue('Left central')
    await wrapper.findAll('select')[0].setValue('F3')
    await wrapper.findAll('select')[1].setValue('Cz')
    await wrapper.find('.channel-confirm').trigger('click')
    expect(wrapper.emitted('apply')?.[0]).toEqual([[
      { name: 'Left central', terms: [{ channel: 'F3', weight: 1 }, { channel: 'Cz', weight: -1 }] },
    ]])
  })

  it('阻止同极和空列表', async () => {
    const wrapper = mount(CustomMontageDialog, {
      props: { channels, rows: [{ name: 'F3-Cz', terms: [{ channel: 'F3', weight: 1 }, { channel: 'Cz', weight: -1 }] }] },
    })
    await wrapper.findAll('.custom-montage-term select')[1].setValue('F3')
    expect(wrapper.text()).toContain('不能重复使用通道')
    expect(wrapper.find('.channel-confirm').attributes('disabled')).toBeDefined()
    await wrapper.find('.montage-remove').trigger('click')
    expect(wrapper.text()).toContain('至少需要一条')
  })

  it('取消时不提交或修改原定义', async () => {
    const original = [{ name: 'F3-Cz', terms: [{ channel: 'F3', weight: 1 }, { channel: 'Cz', weight: -1 }] }]
    const wrapper = mount(CustomMontageDialog, { props: { channels, rows: original } })
    await wrapper.find('input').setValue('Changed')
    await wrapper.findAll('.channel-dialog-footer button')[0].trigger('click')
    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(wrapper.emitted('apply')).toBeUndefined()
    expect(original[0].name).toBe('F3-Cz')
  })
})
