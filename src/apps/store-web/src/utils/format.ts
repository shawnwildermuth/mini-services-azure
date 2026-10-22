const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export const money = (amount: number) => usd.format(amount)

/** Display name for a spec key such as "formFactor" -> "Form factor". */
export function specLabel(name: string): string {
  const spaced = name.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}
