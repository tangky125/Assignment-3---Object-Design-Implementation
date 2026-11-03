// Scenario 1 at component level: a customer logs a request with mistakes, corrects them,
// removes a parcel on the review step and submits. The API is replaced by a fake.
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { session } from '@/session/session'

const submit = vi.fn()

vi.mock('@/api/dms', () => ({
  requestApi: {
    getCustomer: vi.fn(async () => ({
      customerId: 'C001',
      name: 'Jane Citizen',
      phone: '0412 345 678',
      email: null,
    })),
    recentAddresses: vi.fn(async () => []),
    checkPickup: vi.fn(async (a: { postcode: string }) =>
      a.postcode === '2000'
        ? { succeeded: false, message: 'Postcode 2000 is outside our Greater Melbourne service area.' }
        : { succeeded: true, message: 'ok' },
    ),
    submit: (...args: unknown[]) => submit(...args),
  },
  authApi: { lifecycle: vi.fn(async () => ({})), me: vi.fn() },
}))

import LogRequestView from '@/views/requests/LogRequestView.vue'

function tomorrowInput(): string {
  const d = new Date(Date.now() + 86_400_000)
  const pad = (n: number): string => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T09:00`
}

async function click(wrapper: VueWrapper, text: string): Promise<void> {
  const button = wrapper.findAll('button').find((b) => b.text() === text)
  if (!button) throw new Error(`No button "${text}"`)
  await button.trigger('click')
  await flushPromises()
}

async function continueStep(wrapper: VueWrapper): Promise<void> {
  await wrapper.get('form').trigger('submit')
  await flushPromises()
}

describe('Log a delivery request', () => {
  beforeEach(() => {
    session.user = {
      username: 'jane',
      displayName: 'Jane Citizen',
      roles: ['Customer'],
      customerId: 'C001',
      staffId: null,
    }
    submit.mockReset()
    submit.mockResolvedValue({
      succeeded: true,
      message: 'Request DMS-2026-0009 submitted.',
      value: 'DMS-2026-0009',
    })
  })

  it('validates each step, allows changes, and submits', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:p(.*)*', component: { render: () => null } }],
    })
    const wrapper = mount(LogRequestView, { global: { plugins: [router] } })
    await flushPromises()

    expect(wrapper.text()).toContain('Logging a request for Jane Citizen')
    await click(wrapper, 'Continue')

    // Step 2: empty fields are rejected with a message under each.
    await continueStep(wrapper)
    expect(wrapper.text()).toContain('Street address cannot be blank.')
    expect(wrapper.text()).toContain('Postcode cannot be blank.')

    await wrapper.get('#pickup-street').setValue('10 Swanston St')
    await wrapper.get('#pickup-suburb').setValue('Melbourne')
    await wrapper.get('#pickup-postcode').setValue('2000')
    await wrapper.get('#ready').setValue(tomorrowInput())
    await continueStep(wrapper)
    expect(wrapper.text()).toContain('Postcode 2000 is outside our Greater Melbourne service area.')

    await wrapper.get('#pickup-postcode').setValue('3000')
    await continueStep(wrapper)
    expect(wrapper.text()).toContain('Step 3 of 6')

    // Step 3: a bad phone number is rejected.
    await wrapper.get('#dest-street').setValue('5 Chapel St')
    await wrapper.get('#dest-suburb').setValue('Windsor')
    await wrapper.get('#dest-postcode').setValue('3181')
    await wrapper.get('#recipient-name').setValue('Tom Lee')
    await wrapper.get('#recipient-phone').setValue('12345')
    await continueStep(wrapper)
    expect(wrapper.text()).toContain('Enter an Australian phone number')
    await wrapper.get('#recipient-phone').setValue('0498765432')
    await continueStep(wrapper)

    // Step 4: a parcel needs a size; add a second parcel.
    await continueStep(wrapper)
    expect(wrapper.text()).toContain('Choose Small, Medium or Large.')
    await wrapper.findAll('input[type=radio]')[0].setValue(true)
    await click(wrapper, '+ Add another parcel')
    await wrapper.findAll('[data-testid=parcel]')[1].findAll('input[type=radio]')[1].setValue(true)
    await continueStep(wrapper)
    await continueStep(wrapper)

    // Review: remove parcel 2 (change of mind), then submit.
    expect(wrapper.text()).toContain('Step 6 of 6: Review and submit')
    expect(wrapper.findAll('[data-testid=review-parcel]')).toHaveLength(2)
    await click(wrapper, 'Remove parcel 2')
    expect(wrapper.findAll('[data-testid=review-parcel]')).toHaveLength(1)
    expect(wrapper.text()).toContain('Parcel 2 removed')

    await click(wrapper, 'Submit request')
    expect(submit).toHaveBeenCalledTimes(1)
    const draft = submit.mock.calls[0][0]
    expect(draft.parcels).toHaveLength(1)
    expect(draft.recipient.phone).toBe('0498 765 432')
    expect(wrapper.get('[data-testid=reference]').text()).toBe('DMS-2026-0009')
  })

  it('keeps at least one parcel', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:p(.*)*', component: { render: () => null } }],
    })
    const wrapper = mount(LogRequestView, { global: { plugins: [router] } })
    await flushPromises()
    await click(wrapper, 'Continue')
    ;(wrapper.vm as unknown as { step: number }).step = 3
    await flushPromises()
    await click(wrapper, 'Remove parcel 1')
    expect(wrapper.text()).toContain('A request needs at least one parcel')
  })
})
