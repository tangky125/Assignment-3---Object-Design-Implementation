// Format checks for everything the user types. Each rule returns null when the value is
// acceptable, or a plain, actionable message saying what to type instead (A1 design-level
// requirement "Actionable Error Handling"). The server repeats these checks (Api/Validation),
// and business rules such as the service area stay on the server.

export type Rule = (value: string) => string | null

const namePattern = /^[A-Za-z0-9 .,'&-]+$/
const emailPattern = /^[^@\s]+@[^@\s]+\.[^@\s]+$/
const suburbPattern = /^[A-Za-z][A-Za-z '-]{1,39}$/
const postcodePattern = /^\d{4}$/
const referencePattern = /^DMS-\d{4}-\d{4}$/i

/** A person or business name: 2-60 characters with at least one letter. */
export const name: Rule = (value) => {
  const v = value.trim()
  if (v.length === 0) return 'Name cannot be blank.'
  if (v.length < 2 || v.length > 60) return 'Name must be 2 to 60 characters long.'
  if (!namePattern.test(v) || !/[A-Za-z]/.test(v))
    return "Name can contain letters, numbers, spaces and . , ' & - only."
  return null
}

/** Returns an Australian number in standard spacing (0412 345 678 / 03 9123 4567), or null if invalid. */
export function normalisePhone(value: string): string | null {
  let digits = value.replace(/\D/g, '')
  if (value.trim().startsWith('+61') && digits.length === 11) {
    digits = '0' + digits.slice(2)
  }
  if (digits.length !== 10) return null
  if (digits.startsWith('04')) return `${digits.slice(0, 4)} ${digits.slice(4, 7)} ${digits.slice(7)}`
  if (/^0[2378]/.test(digits)) return `${digits.slice(0, 2)} ${digits.slice(2, 6)} ${digits.slice(6)}`
  return null
}

/** An Australian mobile or landline number. */
export const phone: Rule = (value) => {
  if (value.trim().length === 0) return 'Phone number cannot be blank.'
  return normalisePhone(value) === null
    ? 'Enter an Australian phone number with 10 digits, e.g. 0412 345 678 or 03 9123 4567.'
    : null
}

/** An optional email address. */
export const optionalEmail: Rule = (value) => {
  const v = value.trim()
  if (v.length === 0) return null
  return emailPattern.test(v)
    ? null
    : 'Enter a valid email address, e.g. jane@example.com, or leave it blank.'
}

/** A street line: 3-80 characters including a letter. */
export const street: Rule = (value) => {
  const v = value.trim()
  if (v.length === 0) return 'Street address cannot be blank.'
  if (v.length < 3 || v.length > 80 || !/[A-Za-z]/.test(v)) {
    return 'Enter a street address of 3 to 80 characters, e.g. 10 Swanston St.'
  }
  return null
}

/** A suburb: letters, spaces, apostrophes and hyphens. */
export const suburb: Rule = (value) => {
  const v = value.trim()
  if (v.length === 0) return 'Suburb cannot be blank.'
  return suburbPattern.test(v) ? null : 'Suburb must be 2 to 40 letters, e.g. Melbourne or Box Hill.'
}

/** An Australian postcode: exactly four digits. */
export const postcode: Rule = (value) => {
  const v = value.trim()
  if (v.length === 0) return 'Postcode cannot be blank.'
  return postcodePattern.test(v) ? null : 'Postcode must be exactly 4 digits, e.g. 3000.'
}

/** A reason or explanation: 3-200 characters. */
export const reason: Rule = (value) => {
  const v = value.trim()
  if (v.length === 0) return 'Please give a reason; it cannot be blank.'
  return v.length < 3 || v.length > 200 ? 'Reason must be 3 to 200 characters.' : null
}

/** Optional free text of at most maxLength characters. */
export function optionalText(maxLength: number): Rule {
  return (value) =>
    value.trim().length > maxLength ? `Keep this to ${maxLength} characters or fewer.` : null
}

/** A request reference such as DMS-2026-0001. */
export const reference: Rule = (value) => {
  const v = value.trim()
  if (v.length === 0) return 'Reference cannot be blank.'
  return referencePattern.test(v) ? null : 'A reference looks like DMS-2026-0001.'
}

/** Parses money such as 27.5, $27.50 or 1,000.00; returns null when it is not a positive number. */
export function parseMoney(value: string): number | null {
  const cleaned = value.replace(/[$,\s]/g, '')
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) return null
  return Math.round(Number(cleaned) * 100) / 100
}

/** An amount of money within a range. */
export function money(min: number, max: number): Rule {
  return (value) => {
    if (value.trim().length === 0) return 'Enter an amount in dollars.'
    const amount = parseMoney(value)
    if (amount === null) return 'Enter an amount in dollars using numbers only, e.g. 27.50.'
    if (amount < min || amount > max) return `Amount must be between ${formatAud(min)} and ${formatAud(max)}.`
    return null
  }
}

/** Parses the value of an <input type="datetime-local"> ("2026-11-03T09:00"). */
export function parseLocalDateTime(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value.trim())
  if (match === null) return null
  const [, y, mo, d, h, mi] = match.map(Number)
  const date = new Date(y, mo - 1, d, h, mi)
  // Reject impossible dates such as 31/02, which Date would silently roll over.
  return date.getFullYear() === y && date.getMonth() === mo - 1 && date.getDate() === d ? date : null
}

/** A required date and time, checked by an extra rule. */
export function dateTime(extra: (date: Date) => string | null): Rule {
  return (value) => {
    if (value.trim().length === 0) return 'Enter a date and time.'
    const date = parseLocalDateTime(value)
    return date === null ? 'Enter a real date and time, e.g. 04/11/2026 09:30.' : extra(date)
  }
}

/** Runs a set of named rules and returns the errors, keyed by field name. */
export function check<K extends string>(fields: Record<K, [string, Rule]>): Partial<Record<K, string>> {
  const errors: Partial<Record<K, string>> = {}
  for (const key of Object.keys(fields) as K[]) {
    const [value, rule] = fields[key]
    const error = rule(value)
    if (error !== null) errors[key] = error
  }
  return errors
}

function formatAud(amount: number): string {
  return '$' + amount.toLocaleString('en-AU', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}
