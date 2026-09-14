// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import UserAlgorithmBuilder from './UserAlgorithmBuilder.vue'

const { validateDefinition, createDefinition, createDefinitionVersion } = vi.hoisted(() => ({
  validateDefinition: vi.fn(async () => ({ valid: true, node_order: ['calculation', 'output'] })),
  createDefinition: vi.fn(async () => ({ definition_id: 'new-definition' })),
  createDefinitionVersion: vi.fn(async () => ({ semver: '1.0.0' })),
}))
vi.mock('../api/algorithmDefinitions', () => ({ validateDefinition, createDefinition, createDefinitionVersion }))

describe('UserAlgorithmBuilder', () => {
  it('builds and persists a readable Theta/Beta formula through the backend', async () => {
    const wrapper = mount(UserAlgorithmBuilder)
    expect(wrapper.text()).toContain('Theta 功率')
    expect(wrapper.text()).toContain('Beta 功率')
    expect(wrapper.text()).toContain('Theta 功率 ÷ Beta 功率')

    await wrapper.get('.primary-action').trigger('click')
    expect(validateDefinition).toHaveBeenCalledOnce()
    expect(createDefinition).toHaveBeenCalledWith('Theta/Beta 比值', expect.any(String))
    expect(createDefinitionVersion).toHaveBeenCalledWith('new-definition', expect.objectContaining({ semver: '1.0.0' }))
  })
})
