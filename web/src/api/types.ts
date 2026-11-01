// Mirrors the DTOs published by the API (see /openapi/v1.json). Money is in bolivianos, dates are
// 'YYYY-MM-DD', times 'HH:mm:ss' (hall-local) and instants are ISO-8601 UTC.

export type HallEventStatus =
  | 'Requested'
  | 'ApprovedByManagement'
  | 'Confirmed'
  | 'InProgress'
  | 'Completed'
  | 'Rejected'
  | 'Expired'
  | 'Cancelled'

export type QuoteStatus = 'Issued' | 'Accepted' | 'Expired'

export interface ClockResponse {
  utcNow: string
  localNow: string
  todayLocal: string
  offsetHours: number
}

export interface JobResult {
  job: string
  changed: number
}

export interface ExtraServiceRequest {
  code: string
  quantity?: number | null
}

export interface QuoteRequest {
  customerId: string
  date: string
  startTime: string
  endTime: string
  attendance: number
  extraServices: ExtraServiceRequest[]
  hallId?: string | null
}

export interface PriceBreakdown {
  baseHourlyRate: number
  hours: number
  rateFactor: number
  hallRental: number
  membershipDiscountPercentage: number
  membershipDiscount: number
  extrasTotal: number
  total: number
}

export interface ExtraServiceLine {
  type: string
  code: string
  name?: string
  description?: string
  quantity: number
  unitCost: number
  lineTotal: number
}

export interface Availability {
  isAvailable: boolean
  code: string | null
  reason: string | null
}

export interface QuotePreview {
  price: PriceBreakdown
  extraServices: ExtraServiceLine[]
  availability: Availability
}

export interface IssuedQuote extends QuotePreview {
  quoteId: string
  issuedAtUtc: string
  expiresAtUtc: string
}

export interface Quote {
  id: string
  customerId: string
  customerName: string | null
  hallId: string
  date: string
  start: string
  end: string
  attendance: number
  status: QuoteStatus
  issuedAtUtc: string
  expiresAtUtc: string
  acceptedAtUtc: string | null
  hoursRemaining: number
  price: PriceBreakdown
  extraServices: ExtraServiceLine[]
}

export interface Deposit {
  percentage: number
  required: number
  accredited: number
  pending: number
  isCovered: boolean
  transactionIds: string[]
}

export interface HistoryEntry {
  status: HallEventStatus
  occurredAtUtc: string
  actor: string
  note: string
}

export interface HallEvent {
  id: string
  quoteId: string
  customerId: string
  customerName: string | null
  hallId: string
  date: string
  start: string
  end: string
  attendance: number
  total: number
  status: HallEventStatus
  deposit: Deposit | null
  requestedAtUtc: string
  decisionDeadlineUtc: string
  depositDeadlineUtc: string | null
  decision: { decidedBy: string; decidedAtUtc: string; approved: boolean; rejectionReason: string | null } | null
  creditGenerated: number
  rescheduleCount: number
  extraServicesAssigned: boolean
  remainingToSettle: number | null
  extraServices: ExtraServiceLine[]
  history: HistoryEntry[]
}

export interface HallEventListItem {
  id: string
  customerId: string
  customerName: string | null
  date: string
  start: string
  end: string
  attendance: number
  total: number
  status: HallEventStatus
  decisionDeadlineUtc: string
  depositDeadlineUtc: string | null
  depositRequired: number
  depositAccredited: number
}

export interface RescheduleResult {
  eventId: string
  status: HallEventStatus
  creditApplied: number
  depositPending: number
  depositDeadlineUtc: string | null
}

export interface CustomerCredit {
  customerId: string
  customerName: string | null
  balance: number
  movements: { type: string; amount: number; eventId: string; reason: string; occurredAtUtc: string }[]
}

export interface CalendarBlock {
  blockId: string
  eventId: string
  date: string
  start: string
  end: string
  bufferStart: string
  bufferEnd: string
  kind: 'Provisional' | 'Definitive'
  equipment: { code: string; quantity: number }[]
  customerId: string | null
  customerName: string | null
  eventStatus: string | null
}

// ---- Simulated external contexts ----

export interface Member {
  memberId: string
  fullName: string
  kind: string
  plan: string | null
  status: string
  discountPct: number
}

export interface Catalog {
  venues: {
    venue_uid: string
    venue_code: string
    display_name: string
    max_capacity: number
    assets: { asset_code: string; label: string; units_in_stock: number; fee_per_event: number }[]
  }[]
  cateringMenu: { sku: string; title: string; price_per_head: number }[]
}

export interface PaymentAttempt {
  txnId: string
  amountCents: number
  status: string
  atUtc: string
}

export interface PaymentLink {
  linkId: string
  merchantRef: string
  amountCents: number
  currencyCode: string
  description: string
  createdAtUtc: string
  status: string
  paidCents: number
  attempts: PaymentAttempt[] | null
}

export interface PayLinkResponse {
  txnId: string
  webhookStatusCode: number
  webhookResponse: Record<string, unknown> | null
  link: PaymentLink
}

export interface AccessCredential {
  credentialId: string
  eventRef: string
  pin: string
  qrText: string
  capacity: number
  validFromUtc: string
  validUntilUtc: string
  status: string
  revokedAtUtc: string | null
}

export interface Notification {
  id: string
  to: string
  channel: string
  subject: string
  body: string
  sentAt: string
}

export interface LedgerEntry {
  entryId: string
  atUtc: string
  kind: string
  account: string
  reference: string
  amount: number
  currency: string
  note: string
}

export interface CateringOrder {
  orderId: string
  eventRef: string
  receivedAtUtc: string
  status: string
  lines: { sku: string; title: string; headcount: number; pricePerHead: number }[]
}

export interface IntegrationEventLogEntry {
  module: string
  id: string
  type: string
  content: unknown
  occurredOnUtc: string
  processedOnUtc: string | null
  attempts: number
  error: string | null
}

export interface InvariantEntry {
  id: string
  description: string
  owner: string
  code: string
  databaseEnforced: boolean
  byDesign: boolean
  hasProbe: boolean
}

export interface ProbeResult {
  invariantId: string
  attempted: boolean
  attempt: string
  rejected: boolean
  code: string | null
  message: string
  statusCode: number
}
