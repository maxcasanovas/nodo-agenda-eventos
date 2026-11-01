// Display helpers. The hall lives in America/La_Paz (UTC-4, no daylight saving); locale es-BO.
const ZONE = 'America/La_Paz'
const LOCALE = 'es-BO'

const moneyFormat = new Intl.NumberFormat(LOCALE, { minimumFractionDigits: 2, maximumFractionDigits: 2 })

export function formatBs(amount: number): string {
  return `Bs ${moneyFormat.format(amount)}`
}

export function formatBsFromCents(cents: number): string {
  return formatBs(cents / 100)
}

/** 'YYYY-MM-DD' is a hall-local calendar date: never pass it through Date's UTC parsing. */
export function formatDate(date: string): string {
  const [year, month, day] = date.split('-').map(Number) as [number, number, number]
  return new Intl.DateTimeFormat(LOCALE, { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' })
    .format(new Date(Date.UTC(year, month - 1, day)))
}

export function formatTime(time: string): string {
  return time.slice(0, 5)
}

export function formatRange(start: string, end: string): string {
  return `${formatTime(start)}–${formatTime(end)}`
}

export function formatInstant(iso: string): string {
  return new Intl.DateTimeFormat(LOCALE, {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: ZONE,
  }).format(new Date(iso))
}

export function formatClock(iso: string): string {
  return new Intl.DateTimeFormat(LOCALE, {
    weekday: 'short',
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: ZONE,
  }).format(new Date(iso))
}

/** Time left until `iso` on the demo clock, e.g. "1 d 3 h", "45 min" or "vencido". */
export function formatRemaining(iso: string, nowIso: string): string {
  const ms = new Date(iso).getTime() - new Date(nowIso).getTime()
  if (ms <= 0) return 'vencido'
  const minutes = Math.floor(ms / 60_000)
  const days = Math.floor(minutes / 1440)
  const hours = Math.floor((minutes % 1440) / 60)
  if (days > 0) return `${days} d ${hours} h`
  if (hours > 0) return `${hours} h ${minutes % 60} min`
  return `${minutes} min`
}

export function addHoursToTime(time: string, hours: number): string {
  const [h = 0, m = 0] = time.split(':').map(Number)
  const total = h + hours
  return `${String(total).padStart(2, '0')}:${String(m).padStart(2, '0')}:00`
}

export function durationHours(start: string, end: string): number {
  return Number(end.slice(0, 2)) - Number(start.slice(0, 2))
}

export function addDays(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number) as [number, number, number]
  const moved = new Date(Date.UTC(year, month - 1, day + days))
  return moved.toISOString().slice(0, 10)
}
