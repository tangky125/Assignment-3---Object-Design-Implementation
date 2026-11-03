// Australian-format display helpers shared by every screen.
import { Handling } from '@/api/types'
import type { RequestDetail, Status, TimeWindow } from '@/api/types'

const statusNames: Record<Status, string> = {
  Requested: 'Requested',
  Quoted: 'Quoted',
  Accepted: 'Accepted',
  Scheduled: 'Scheduled',
  Collected: 'Collected',
  Delivered: 'Delivered',
  DeliveryFailed: 'Delivery failed',
  Cancelled: 'Cancelled',
  Rejected: 'Rejected',
}

/** Readable status, e.g. "Delivery failed". */
export function statusText(status: Status): string {
  return statusNames[status]
}

/** Money, e.g. $1,027.50. */
export function money(amount: number | null | undefined): string {
  if (amount === null || amount === undefined) return '-'
  return '$' + amount.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

function pad(n: number): string {
  return n.toString().padStart(2, '0')
}

const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

/** A date and time, e.g. Tue 03/11/2026 09:30. */
export function when(iso: string | Date): string {
  const d = typeof iso === 'string' ? new Date(iso) : iso
  return `${days[d.getDay()]} ${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** A short date and time, e.g. 03/11 09:30. */
export function shortWhen(iso: string): string {
  const d = new Date(iso)
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** A time only, e.g. 09:30. */
export function timeOf(iso: string): string {
  const d = new Date(iso)
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** A window, e.g. Tue 03/11/2026 09:00 - 11:00. */
export function windowText(w: TimeWindow | null | undefined): string {
  if (!w) return 'Not yet confirmed'
  const start = new Date(w.start)
  const end = new Date(w.end)
  return start.toDateString() === end.toDateString()
    ? `${when(start)} - ${timeOf(w.end)}`
    : `${when(start)} - ${when(end)}`
}

/** Handling flags as text, e.g. "Fragile, Signature required". */
export function handlingText(flags: number): string {
  const names: string[] = []
  if (flags & Handling.Fragile) names.push('Fragile')
  if (flags & Handling.SignatureRequired) names.push('Signature required')
  if (flags & Handling.KeepUpright) names.push('Keep upright')
  return names.length === 0 ? 'None' : names.join(', ')
}

/** Formats a Date for an <input type="datetime-local">. */
export function toLocalInput(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** Converts a server time (ISO) to a datetime-local value. */
export function isoToLocalInput(iso: string): string {
  return toLocalInput(new Date(iso))
}

/** Sends a datetime-local value to the server as local time without a zone ("2026-11-03T09:00:00"). */
export function localInputToServer(value: string): string {
  return `${value}:00`
}

/** True when a scheduled pickup or a collected delivery is past its window. */
export function isLate(detail: RequestDetail, now: Date = new Date()): boolean {
  const q = detail.currentQuote
  if (!q) return false
  if (detail.status === 'Scheduled') return now > new Date(q.pickupWindow.end)
  if (detail.status === 'Collected') return now > new Date(q.deliveryWindow.end)
  return false
}

/** The next whole hour from now. */
export function nextHour(now: Date = new Date()): Date {
  const d = new Date(now)
  d.setMinutes(0, 0, 0)
  d.setHours(d.getHours() + 1)
  return d
}
