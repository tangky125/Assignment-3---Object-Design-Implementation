import { describe, expect, it } from 'vitest'
import { handlingText, isLate, money, statusText, windowText } from '@/utils/format'
import type { RequestDetail } from '@/api/types'

describe('formatting', () => {
  it('shows money the Australian way', () => {
    expect(money(1027.5)).toBe('$1,027.50')
    expect(money(null)).toBe('-')
  })

  it('names statuses and handling flags', () => {
    expect(statusText('DeliveryFailed')).toBe('Delivery failed')
    expect(handlingText(0)).toBe('None')
    expect(handlingText(3)).toBe('Fragile, Signature required')
  })

  it('shows a same-day window with one date', () => {
    expect(windowText({ start: '2026-11-04T09:00:00', end: '2026-11-04T11:00:00' })).toBe(
      'Wed 04/11/2026 09:00 - 11:00',
    )
    expect(windowText(null)).toBe('Not yet confirmed')
  })

  it('flags a scheduled job whose pickup window has passed', () => {
    const detail = {
      status: 'Scheduled',
      currentQuote: {
        pickupWindow: { start: '2026-11-04T09:00:00', end: '2026-11-04T11:00:00' },
        deliveryWindow: { start: '2026-11-04T11:00:00', end: '2026-11-04T14:00:00' },
      },
    } as unknown as RequestDetail
    expect(isLate(detail, new Date(2026, 10, 4, 12, 0))).toBe(true)
    expect(isLate(detail, new Date(2026, 10, 4, 10, 0))).toBe(false)
  })
})
