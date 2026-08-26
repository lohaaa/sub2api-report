export const numberFormatter = new Intl.NumberFormat('zh-CN')
export const costFormatter = new Intl.NumberFormat('zh-CN', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 6,
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

export function formatDate(value: string) {
  return dateFormatter.format(new Date(`${value}T00:00:00Z`))
}

export function formatTimestamp(value: string) {
  return timestampFormatter.format(new Date(value))
}
