import { http, query } from './client'
import type {
  AccessCredential,
  CalendarBlock,
  Availability,
  CateringOrder,
  Catalog,
  ClockResponse,
  CustomerCredit,
  HallEvent,
  HallEventListItem,
  HallEventStatus,
  IntegrationEventLogEntry,
  InvariantEntry,
  IssuedQuote,
  JobResult,
  LedgerEntry,
  Member,
  Notification,
  PayLinkResponse,
  PaymentLink,
  ProbeResult,
  Quote,
  QuotePreview,
  QuoteRequest,
  RescheduleResult,
} from './types'

export const quotesApi = {
  preview: (request: QuoteRequest) => http.post<QuotePreview>('/api/quotes/preview', request),
  issue: (request: QuoteRequest & { performedBy?: string }) => http.post<IssuedQuote>('/api/quotes', request),
  list: (filter: { customerId?: string; status?: string } = {}) => http.get<Quote[]>(`/api/quotes${query(filter)}`),
  accept: (id: string, acceptedBy: string) =>
    http.post<{ quoteId: string; status: string }>(`/api/quotes/${id}/accept`, { acceptedBy }),
}

export const hallEventsApi = {
  list: (filter: { status?: HallEventStatus | ''; customerId?: string } = {}) =>
    http.get<HallEventListItem[]>(`/api/hall-events${query(filter)}`),
  pendingApprovals: () => http.get<HallEventListItem[]>('/api/hall-events/pending-approvals'),
  today: () => http.get<HallEventListItem[]>('/api/hall-events/today'),
  get: (id: string) => http.get<HallEvent>(`/api/hall-events/${id}`),
  approve: (id: string, depositPercentage: number, approvedBy: string) =>
    http.post(`/api/hall-events/${id}/approve`, { depositPercentage, approvedBy }),
  reject: (id: string, reason: string, rejectedBy: string) =>
    http.post(`/api/hall-events/${id}/reject`, { reason, rejectedBy }),
  start: (id: string) => http.post(`/api/hall-events/${id}/start`),
  complete: (id: string) => http.post(`/api/hall-events/${id}/complete`),
  cancel: (id: string, reason: string, cancelledBy: string) =>
    http.post(`/api/hall-events/${id}/cancel`, { reason, cancelledBy }),
  reschedule: (id: string, newDate: string, newStartTime: string, performedBy: string) =>
    http.post<RescheduleResult>(`/api/hall-events/${id}/reschedule`, { newDate, newStartTime, performedBy }),
  credit: (customerId: string) => http.get<CustomerCredit>(`/api/customers/${customerId}/credit`),
}

export const hallScheduleApi = {
  calendar: (hallId: string, from: string, to: string) =>
    http.get<CalendarBlock[]>(`/api/hall-schedule/${hallId}${query({ from, to })}`),
  availability: (hallId: string, date: string, start: string, end: string) =>
    http.get<Availability>(`/api/hall-schedule/${hallId}/availability${query({ date, start, end })}`),
}

export const externalApi = {
  catalog: () => http.get<Catalog>('/api/external/catalog'),
  customers: () => http.get<Member[]>('/api/external/customers'),
  paymentLinks: () => http.get<PaymentLink[]>('/api/external/payment-links'),
  pay: (linkId: string, amount: number | null, decline: boolean) =>
    http.post<PayLinkResponse>(`/api/external/payment-links/${linkId}/pay`, { amount, decline }),
  credentials: () => http.get<AccessCredential[]>('/api/external/access-credentials'),
  notifications: () => http.get<Notification[]>('/api/external/notifications'),
  ledger: () => http.get<LedgerEntry[]>('/api/external/billing/ledger'),
  cateringOrders: () => http.get<CateringOrder[]>('/api/external/catering-orders'),
}

export const demoApi = {
  clock: () => http.get<ClockResponse>('/api/demo/clock'),
  advance: (days: number, hours: number) => http.post<ClockResponse>('/api/demo/clock/advance', { days, hours }),
  reset: () => http.post<ClockResponse>('/api/demo/clock/reset'),
  runJobs: () => http.post<JobResult[]>('/api/demo/jobs/run'),
  integrationEvents: (limit = 200) =>
    http.get<IntegrationEventLogEntry[]>(`/api/demo/integration-events${query({ limit })}`),
  invariants: () => http.get<InvariantEntry[]>('/api/demo/invariants'),
  probe: (id: string) => http.post<ProbeResult>(`/api/demo/invariants/${id}/probe`),
}
