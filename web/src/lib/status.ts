import type { HallEventStatus, QuoteStatus } from '@/api/types'

export const EVENT_STATUS: Record<HallEventStatus, { label: string; tone: string }> = {
  Requested: { label: 'Solicitado', tone: 'bg-sky-100 text-sky-800' },
  ApprovedByManagement: { label: 'Aprobado · espera depósito', tone: 'bg-amber-100 text-amber-800' },
  Confirmed: { label: 'Confirmado', tone: 'bg-emerald-100 text-emerald-800' },
  InProgress: { label: 'En curso', tone: 'bg-violet-100 text-violet-800' },
  Completed: { label: 'Completado', tone: 'bg-slate-200 text-slate-700' },
  Rejected: { label: 'Rechazado', tone: 'bg-red-100 text-red-800' },
  Expired: { label: 'Vencido', tone: 'bg-orange-100 text-orange-800' },
  Cancelled: { label: 'Cancelado', tone: 'bg-red-100 text-red-800' },
}

export const QUOTE_STATUS: Record<QuoteStatus, { label: string; tone: string }> = {
  Issued: { label: 'Vigente', tone: 'bg-sky-100 text-sky-800' },
  Accepted: { label: 'Aceptada', tone: 'bg-emerald-100 text-emerald-800' },
  Expired: { label: 'Vencida', tone: 'bg-orange-100 text-orange-800' },
}

export function eventStatusLabel(status: HallEventStatus): string {
  return EVENT_STATUS[status]?.label ?? status
}
