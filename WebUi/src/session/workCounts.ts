// How much work is waiting for the signed-in staff member, shown on the navigation bar and the
// main menu so new requests, revision requests and held quotes are noticed without opening each
// queue. Counts come from the same queues the screens use, and refresh on every page change and
// once a minute.
import { reactive } from 'vue'
import { quoteApi } from '@/api/dms'
import type { RequestSummary } from '@/api/types'
import { hasRole, isOffice } from './session'

interface WorkCounts {
  /** Requests waiting for a quote; null when the user cannot see that queue. */
  quotes: number | null
  newRequests: number
  revisions: number
  expired: number
  /** Out-of-area quotes waiting for an owner; null when the user is not an owner. */
  ownerApprovals: number | null
}

export const workCounts = reactive<WorkCounts>({
  quotes: null,
  newRequests: 0,
  revisions: 0,
  expired: 0,
  ownerApprovals: null,
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
