import { describe, expect, it } from 'vitest'
import * as rules from '@/validation/rules'

describe('input rules', () => {
  it('accepts Australian mobiles and landlines and formats them', () => {
    expect(rules.phone('0412 345 678')).toBeNull()
    expect(rules.phone('03 9123 4567')).toBeNull()
    expect(rules.phone('+61 412 345 678')).toBeNull()
    expect(rules.normalisePhone('0412345678')).toBe('0412 345 678')
    expect(rules.normalisePhone('0391234567')).toBe('03 9123 4567')
  })

  it('rejects phone numbers that are not Australian', () => {
    expect(rules.phone('')).toBe('Phone number cannot be blank.')
    expect(rules.phone('12345')).toContain('Enter an Australian phone number')
    expect(rules.phone('0512 345 678')).toContain('Enter an Australian phone number')
  })

  it('requires a four-digit postcode', () => {
    expect(rules.postcode('3000')).toBeNull()
    expect(rules.postcode('300')).toContain('exactly 4 digits')
    expect(rules.postcode('30a0')).toContain('exactly 4 digits')
    expect(rules.postcode(' ')).toBe('Postcode cannot be blank.')
  })

  it('checks names', () => {
    expect(rules.name('Jane Citizen')).toBeNull()
    expect(rules.name('Acme Florist Pty Ltd')).toBeNull()
    expect(rules.name('')).toBe('Name cannot be blank.')
    expect(rules.name('!!')).not.toBeNull()
    expect(rules.name('a'.repeat(61))).toContain('2 to 60')
  })

  it('checks streets, suburbs, reasons, references and emails', () => {
    expect(rules.street('10 Swanston St')).toBeNull()
    expect(rules.street('10')).not.toBeNull()
    expect(rules.suburb('Box Hill')).toBeNull()
    expect(rules.suburb('3000')).not.toBeNull()
    expect(rules.reason('ok')).toContain('3 to 200')
    expect(rules.reason('Price too high')).toBeNull()
    expect(rules.reference('dms-2026-0001')).toBeNull()
    expect(rules.reference('DMS-26-1')).toContain('DMS-2026-0001')
    expect(rules.optionalEmail('')).toBeNull()
    expect(rules.optionalEmail('jane@example.com')).toBeNull()
    expect(rules.optionalEmail('jane@')).not.toBeNull()
  })

  it('parses money and checks its range', () => {
    expect(rules.parseMoney('$1,027.50')).toBe(1027.5)
    expect(rules.parseMoney('27.5')).toBe(27.5)
    expect(rules.parseMoney('abc')).toBeNull()
    expect(rules.parseMoney('-5')).toBeNull()
    expect(rules.money(1, 10_000)('0.50')).toContain('between $1.00 and $10,000.00')
    expect(rules.money(1, 10_000)('25')).toBeNull()
  })

  it('parses date-times and rejects impossible dates', () => {
    expect(rules.parseLocalDateTime('2026-11-04T09:30')).toEqual(new Date(2026, 10, 4, 9, 30))
    expect(rules.parseLocalDateTime('2026-02-31T10:00')).toBeNull()
    expect(rules.dateTime(() => null)('')).toBe('Enter a date and time.')
  })

  it('collects errors by field', () => {
    const errors = rules.check({ name: ['', rules.name], phone: ['0412345678', rules.phone] })
    expect(errors).toEqual({ name: 'Name cannot be blank.' })
  })
})
