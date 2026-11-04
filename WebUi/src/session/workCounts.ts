// How much is waiting for the signed-in user, shown on the navigation bar and the main menu: for
// office staff, new requests, revision requests and held quotes; for a customer, quotes waiting for
// their decision. Nothing has to be opened to notice them. Counts come from the same queues the screens use, and refresh on every page change and
// once a minute.
import { reactive } from 'vue'
import { quoteApi } from '@/api/dms'
import type { RequestSummary } from '@/api/types'
import { hasRole, isOffice, session } from './session'

interface WorkCounts {
  /** Requests waiting for a quote; null when the user cannot see that queue. */
  quotes: number | null
  newRequests: number
  revisions: number
  expired: number
  /** Out-of-area quotes waiting for an owner; null when the user is not an owner. */
  ownerApprovals: number | null
  /** Quotes waiting for the signed-in customer's decision; null when the user is not a customer. */
  decisions: number | null
  /** The decisions, most urgent first, for the main-menu summary. */
  decisionRows: RequestSummary[]
}

export const workCounts = reactive<WorkCounts>({
  quotes: null,
  newRequests: 0,
  revisions: 0,
  expired: 0,
  ownerApprovals: null,
  decisions: null,
  decisionRows: [],
})

function countTag(rows: RequestSummary[], tag: RequestSummary['tag']): number {
  return rows.filter((r) => r.tag === tag).length
}

/** Reloads the counts for the current user; failures leave the last counts in place. */
export async function refreshWorkCounts(): Promise<void> {
  try {
    if (isOffice.value) {
      const queue = await quoteApi.queue()
      workCounts.quotes = queue.length
      workCounts.newRequests = countTag(queue, 'New')
      workCounts.revisions = countTag(queue, 'Revision')
      workCounts.expired = countTag(queue, 'Expired')
    } else {
      workCounts.quotes = null
    }
    workCounts.ownerApprovals = hasRole('Owner') ? (await quoteApi.ownerQueue()).length : null
    const customerId = session.user?.customerId
    if (hasRole('Customer') && customerId) {
      const mine = await quoteApi.requestsFor(customerId)
      const waiting = mine.filter((r) => r.tag === 'QuoteReady' || r.tag === 'ExpiresSoon')
      // Quotes about to expire first.
      waiting.sort((a, b) => Number(b.tag === 'ExpiresSoon') - Number(a.tag === 'ExpiresSoon'))
      workCounts.decisions = waiting.length
      workCounts.decisionRows = waiting
    } else {
      workCounts.decisions = null
      workCounts.decisionRows = []
    }
  } catch {
    // The counts are a convenience; the queues themselves show any error.
  }
}

/** Plain-English summary of the quote queue, e.g. "3 waiting: 1 new, 1 revision asked, 1 expired". */
export function quoteQueueSummary(): string | null {
  if (workCounts.quotes === null) return null
  if (workCounts.quotes === 0) return 'Nothing waiting for a quote.'
  const parts: string[] = []
  if (workCounts.newRequests > 0) parts.push(`${workCounts.newRequests} new`)
  if (workCounts.revisions > 0)
    parts.push(`${workCounts.revisions} revision${workCounts.revisions === 1 ? '' : 's'} asked`)
  if (workCounts.expired > 0) parts.push(`${workCounts.expired} expired`)
  const noun = workCounts.quotes === 1 ? 'request' : 'requests'
  return `${workCounts.quotes} ${noun} waiting for a quote: ${parts.join(', ')}.`
}

/** e.g. "1 quote ready for you to decide: DMS-2026-0001, $36.00, decide by Thu 05/11/2026 16:19." */
export function decisionSummary(): string | null {
  if (workCounts.decisions === null) return null
  if (workCounts.decisions === 0) return 'Nothing waiting for your decision.'
  const first = workCounts.decisionRows[0]
  const detail = first ? `${first.reference}, ${first.note ?? ''}` : ''
  if (workCounts.decisions === 1) return `1 quote ready for you to decide: ${detail}.`
  const lead = first?.tag === 'ExpiresSoon' ? 'Expiring first' : 'First'
  return `${workCounts.decisions} quotes ready for you to decide. ${lead}: ${detail}.`
}
