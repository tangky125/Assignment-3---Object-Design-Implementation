// TypeScript mirror of the C# records in Api/Contracts and Api/ApiModels.cs.
// The server sends enums as their names (e.g. "DeliveryFailed"), except ParcelHandling,
// which is a number so several flags can be combined (1 + 2 = fragile and signature).

/** The closed set of nine statuses (owned by Domain/StatusLifecycle). */
export type Status =
  | 'Requested'
  | 'Quoted'
  | 'Accepted'
  | 'Scheduled'
  | 'Collected'
  | 'Delivered'
  | 'DeliveryFailed'
  | 'Cancelled'
  | 'Rejected'

export type UserRole = 'Customer' | 'OfficeStaff' | 'DeliveryStaff' | 'Owner'

export type SizeBand = 'Small' | 'Medium' | 'Large'

export type ViewerLevel = 'None' | 'Recipient' | 'Customer' | 'Staff'

/** Bit flags, matching the C# [Flags] enum ParcelHandling. */
export const Handling = {
  None: 0,
  Fragile: 1,
  SignatureRequired: 2,
  KeepUpright: 4,
} as const

export interface SessionUser {
  username: string
  displayName: string
  roles: UserRole[]
  customerId: string | null
  staffId: string | null
}

export interface SignInResponse {
  token: string
  user: SessionUser
}

export interface OperationResult {
  succeeded: boolean
  message: string
}

export interface ValueResult<T> extends OperationResult {
  value: T | null
}

export interface AddressInput {
  street: string
  suburb: string
  postcode: string
}

export interface ContactInput {
  name: string
  phone: string
}

export interface ParcelInput {
  size: SizeBand
  flags: number
  description: string | null
}

export interface RequestDraft {
  customerId: string
  pickup: AddressInput
  pickupContact: ContactInput
  readyTime: string
  destination: AddressInput
  recipient: ContactInput
  parcels: ParcelInput[]
  notes: string | null
}

export interface CustomerSummary {
  customerId: string
  name: string
  phone: string
  email: string | null
}

export interface AddressPair {
  pickup: AddressInput
  destination: AddressInput
  recipient: ContactInput
}

export interface TimeWindow {
  start: string
  end: string
}

export interface RequestSummary {
  reference: string
  customerName: string
  status: Status
  loggedAt: string
  pickupSuburb: string
  destinationSuburb: string
  parcelCount: number
  pickupWindow: TimeWindow | null
  note: string | null
  /** Why the row is in the quote queue; absent on other lists. */
  tag?: QueueTag | null
}

export type QueueTag = 'New' | 'Revision' | 'Expired'

export interface ParcelView {
  parcelId: string
  size: SizeBand
  flags: number
  description: string | null
  collected: boolean
}

export interface QuoteView {
  reference: string
  version: number
  price: number | null
  adjustReason: string | null
  pickupWindow: TimeWindow
  deliveryWindow: TimeWindow
  issuedAt: string
  validUntil: string
  isExpired: boolean
  awaitingOwnerApproval: boolean
  outcome: string
  decision?: 'Pending' | 'Accepted' | 'Rejected' | 'RevisionRequested' | 'Expired' | null
  decisionReason?: string | null
  confirmedVia?: string | null
}

export interface PaymentView {
  at: string
  method: string
  amount: number
  successful: boolean
  outcome: string
}

export interface EventView {
  at: string
  from: Status | null
  to: Status
  actor: string
  note: string
}

export interface RequestDetail {
  reference: string
  customerId: string
  customerName: string
  customerPhone: string
  status: Status
  loggedAt: string
  loggedBy: string
  pickup: AddressInput
  pickupContact: ContactInput
  readyTime: string
  destination: AddressInput
  recipient: ContactInput
  parcels: ParcelView[]
  notes: string | null
  currentQuote: QuoteView | null
  allocatedStaffId: string | null
  allocatedStaffName: string | null
  payments: PaymentView[]
  history: EventView[]
}

export interface RecipientView {
  reference: string
  status: Status
  expectedDelivery: TimeWindow | null
  destinationSuburb: string
  late: boolean
}

export interface TrackingResponse {
  level: ViewerLevel
  detail: RequestDetail | null
  recipient: RecipientView | null
}

export interface QuoteProposal {
  reference: string
  proposedPrice: number | null
  needsOwnerApproval: boolean
  pickupWindow: TimeWindow
  deliveryWindow: TimeWindow
  priceBreakdown: string[]
  warnings: string[]
}

export interface QuoteInput {
  adjustedPrice: number | null
  adjustReason: string | null
  pickupWindow: TimeWindow
  deliveryWindow: TimeWindow
}

export interface StaffOption {
  staffId: string
  name: string
  eligible: boolean
  reason: string
}

export interface PaymentOutcome {
  successful: boolean
  message: string
  changeDue: number | null
}

export interface JobResponse {
  detail: RequestDetail
  amountDue: number
}

/** Legal next statuses for each status, as published by the server's StatusLifecycle. */
export type LifecycleTable = Record<Status, Status[]>
