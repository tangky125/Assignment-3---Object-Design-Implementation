// A5 at component level (Area 4): the driver cannot tick a parcel or complete the delivery
// before the customer has paid, and a job that is already Collected without a payment can still
// be paid for instead of being stranded. The API is replaced by a fake.
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { JobResponse, PaymentView, Status } from '@/api/types'

const get = vi.fn()

vi.mock('@/api/dms', () => ({
  jobApi: {
    get: (...args: unknown[]) => get(...args),
    setCollected: vi.fn(),
    cash: vi.fn(),
    card: vi.fn(),
    confirmCollection: vi.fn(),
    delivered: vi.fn(),
    failure: vi.fn(),
  },
  trackingApi: { find: vi.fn() },
  quoteApi: {},
  requestApi: {},
  allocationApi: {},
  authApi: {},
}))

import JobView from '@/views/jobs/JobView.vue'

/** A job as the API returns it: same shape, different status/payment combinations. */
function job(status: Status, payments: PaymentView[], amountDue = 36): JobResponse {
  const start = new Date(Date.now() + 3_600_000).toISOString()
  const end = new Date(Date.now() + 7_200_000).toISOString()
  return {
    amountDue,
    detail: {
      reference: 'DMS-2026-0006',
      customerId: 'C001',
      customerName: 'Jane Citizen',
      customerPhone: '0412 345 678',
      status,
      loggedAt: new Date().toISOString(),
      loggedBy: 'Olivia Brown',
      pickup: { street: '10 Swanston Street', suburb: 'Melbourne', postcode: '3000' },
      pickupContact: { name: 'Jane Citizen', phone: '0412 345 678' },
      readyTime: start,
      destination: { street: '5 Collins Street', suburb: 'Melbourne', postcode: '3000' },
      recipient: { name: 'Mia Wong', phone: '0498 765 432' },
      parcels: [
        {
          parcelId: 'P1',
          size: 'Medium',
          flags: 0,
          description: 'Flowers',
          collected: status === 'Collected',
        },
      ],
      notes: null,
      currentQuote: {
        reference: 'DMS-2026-0006',
        version: 1,
        price: amountDue,
        adjustReason: null,
        pickupWindow: { start, end },
        deliveryWindow: { start, end },
        issuedAt: new Date().toISOString(),
        validUntil: end,
        isExpired: false,
        awaitingOwnerApproval: false,
        outcome: 'Issued, awaiting decision',
      },
      allocatedStaffId: 'S003',
      allocatedStaffName: 'Ben Nguyen',
      payments,
      history: [],
    },
  }
}

const cashPaid: PaymentView = {
  at: new Date().toISOString(),
  method: 'Cash',
  amount: 36,
  successful: true,
  outcome: 'Approved',
}

const cardDeclined: PaymentView = {
  at: new Date().toISOString(),
  method: 'Card',
  amount: 36,
  successful: false,
  outcome: 'Declined',
}

async function openJob(payload: JobResponse): Promise<VueWrapper> {
  get.mockResolvedValue(payload)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/jobs/:reference', name: 'job', component: JobView },
      { path: '/:p(.*)*', component: { render: () => null } },
    ],
  })
  await router.push('/jobs/DMS-2026-0006')
  await router.isReady()
  const wrapper = mount(JobView, { global: { plugins: [router] } })
  await flushPromises()
  return wrapper
}

function button(wrapper: VueWrapper, text: string) {
  return wrapper.findAll('button').find((b) => b.text() === text)
}

describe('Job page payment rule (A5)', () => {
  beforeEach(() => {
    get.mockReset()
  })

  it('keeps the parcel checkboxes locked until the customer pays', async () => {
    const wrapper = await openJob(job('Scheduled', []))

    expect(wrapper.get('[data-testid=payment-first-hint]').text()).toContain('Take payment first')
    expect(wrapper.get('[data-testid=job-parcel] input[type=checkbox]').attributes('disabled')).toBeDefined()
    // Payment is still offered, and a failure can always be recorded.
    expect(button(wrapper, 'Take cash')).toBeDefined()
    expect(button(wrapper, 'Take card')).toBeDefined()
    expect(button(wrapper, 'Record a failed pickup')?.attributes('disabled')).toBeUndefined()
  })

  it('does not unlock the checkboxes for a declined card', async () => {
    const wrapper = await openJob(job('Scheduled', [cardDeclined]))

    expect(wrapper.find('[data-testid=payment-first-hint]').exists()).toBe(true)
    expect(wrapper.get('[data-testid=job-parcel] input[type=checkbox]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('Not yet paid')
  })

  it('unlocks the checkboxes once the payment is recorded', async () => {
    const wrapper = await openJob(job('Scheduled', [cashPaid]))

    expect(wrapper.find('[data-testid=payment-first-hint]').exists()).toBe(false)
    expect(wrapper.get('[data-testid=job-parcel] input[type=checkbox]').attributes('disabled')).toBeUndefined()
    expect(button(wrapper, 'Take cash')).toBeUndefined()
    expect(wrapper.text()).toContain('Paid by Cash')
  })

  it('still offers payment in stage 2 when a Collected job was never paid', async () => {
    const wrapper = await openJob(job('Collected', []))

    expect(wrapper.text()).toContain('Stage 2 of 2')
    expect(button(wrapper, 'Take cash')).toBeDefined()
    expect(button(wrapper, 'Take card')).toBeDefined()
    expect(wrapper.get('[data-testid=record-delivered]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid=pay-before-handover]').text()).toContain(
      'Payment must be collected before the parcels are handed over',
    )
  })

  it('allows the hand-over once a Collected job is paid', async () => {
    const wrapper = await openJob(job('Collected', [cashPaid]))

    expect(wrapper.get('[data-testid=record-delivered]').attributes('disabled')).toBeUndefined()
    expect(wrapper.find('[data-testid=pay-before-handover]').exists()).toBe(false)
    expect(button(wrapper, 'Take cash')).toBeUndefined()
  })
})
