export type Carrier = 'UPS' | 'DHL'

const NORTH_AMERICA = new Set(['US', 'CA', 'MX'])

/** UPS ships within North America; everything else goes DHL. Mirrors the fulfillment rule. */
export const carrierFor = (countryCode: string): Carrier =>
  NORTH_AMERICA.has(countryCode.trim().toUpperCase()) ? 'UPS' : 'DHL'
