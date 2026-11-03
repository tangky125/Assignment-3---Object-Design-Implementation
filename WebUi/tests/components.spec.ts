import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import FormField from '@/components/FormField.vue'
import MessageBanner from '@/components/MessageBanner.vue'
import ConfirmBox from '@/components/ConfirmBox.vue'

describe('shared components', () => {
  it('FormField links the label, hint and error to the input', () => {
    const wrapper = mount(FormField, {
      props: {
        id: 'phone',
        label: 'Phone',
        hint: 'e.g. 0412 345 678',
        error: 'Phone number cannot be blank.',
        required: true,
      },
      slots: {
        default: `<template #default="f"><input :id="f.id" :aria-describedby="f.describedBy" :aria-invalid="f.invalid" /></template>`,
      },
    })
    const input = wrapper.get('input')
    expect(wrapper.get('label').attributes('for')).toBe('phone')
    expect(input.attributes('aria-describedby')).toBe('phone-hint phone-error')
    expect(input.attributes('aria-invalid')).toBe('true')
    expect(wrapper.get('[data-testid=field-error]').text()).toBe('Phone number cannot be blank.')
  })

  it('MessageBanner labels each kind in words, not only colour', () => {
    const error = mount(MessageBanner, { props: { kind: 'error', text: 'Card declined.' } })
    expect(error.attributes('role')).toBe('alert')
    expect(error.text()).toContain('Error:')
    const done = mount(MessageBanner, { props: { kind: 'success', text: 'Saved.' } })
    expect(done.attributes('role')).toBe('status')
    expect(done.text()).toContain('Done:')
  })

  it('ConfirmBox lets the user go ahead or change their mind', async () => {
    const wrapper = mount(ConfirmBox, { props: { question: 'Accept this quote?' } })
    const [yes, no] = wrapper.findAll('button')
    await yes.trigger('click')
    await no.trigger('click')
    expect(wrapper.emitted('confirm')).toHaveLength(1)
    expect(wrapper.emitted('cancel')).toHaveLength(1)
  })
})
