import { describe, expect, it } from 'vitest'
import { problemMessage } from '@/api/errors'
import { money, specLabel } from './format'
import { carrierFor } from './shipping'

describe('carrierFor', () => {
  it('uses UPS for North America', () => {
    for (const code of ['US', 'CA', 'MX', 'us', ' ca ']) expect(carrierFor(code)).toBe('UPS')
  })

  it('uses DHL everywhere else', () => {
    for (const code of ['GB', 'DE', 'JP', 'AU', 'BR']) expect(carrierFor(code)).toBe('DHL')
  })
})

describe('format', () => {
  it('formats money as USD', () => {
    expect(money(1999)).toBe('$1,999.00')
    expect(money(0.5)).toBe('$0.50')
  })

  it('turns spec keys into labels', () => {
    expect(specLabel('formFactor')).toBe('Form factor')
    expect(specLabel('tdp')).toBe('Tdp')
  })
})

describe('problemMessage', () => {
  it('flattens validation errors, de-duplicated', () => {
    const error = { errors: { PasswordTooShort: ['Too short.'], PasswordRequiresDigit: ['Needs a digit.', 'Too short.'] } }
    expect(problemMessage(error)).toBe('Too short. Needs a digit.')
  })

  it('falls back to detail, then title, then the fallback', () => {
    expect(problemMessage({ detail: 'Nope', title: 'Bad' })).toBe('Nope')
    expect(problemMessage({ title: 'Bad' })).toBe('Bad')
    expect(problemMessage({}, 'Custom')).toBe('Custom')
    expect(problemMessage(undefined)).toMatch(/something went wrong/i)
  })
})
