export const numberFormatter = new Intl.NumberFormat('zh-CN')
export const costFormatter = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 6,
})
export const usdFormatter = new Intl.NumberFormat('zh-CN', {
  style: 'currency',
  currency: 'USD',
  currencyDisplay: 'narrowSymbol',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})
export const timestampFormatter = new Intl.DateTimeFormat('zh-CN', {
  dateStyle: 'medium',
  timeStyle: 'short',
})
export const dateFormatter = new Intl.DateTimeFormat('zh-CN', {
  dateStyle: 'medium',
  timeZone: 'UTC',
})

export function formatCount(value: string) {
  try {
    return numberFormatter.format(BigInt(value))
  }
  catch {
    return value
  }
}

export function formatCost(value: string | number) {
  return costFormatter.format(Number(value))
}

export function formatUsd(value: string | number) {
  return usdFormatter.format(Number(value))
}

export function formatDate(value: string) {
  return dateFormatter.format(new Date(`${value}T00:00:00Z`))
}

export function formatTimestamp(value: string) {
  return timestampFormatter.format(new Date(value))
}

/**
 * Converts a half-open exclusive end date (YYYY-MM-DD) into the user-visible
 * closed end date by subtracting one day with UTC date arithmetic only.
 */
export function toInclusiveEndDate(exclusiveEndDate: string) {
  const date = new Date(`${exclusiveEndDate}T00:00:00Z`)
  if (Number.isNaN(date.getTime())) {
    return exclusiveEndDate
  }
  date.setUTCDate(date.getUTCDate() - 1)
  return date.toISOString().slice(0, 10)
}
