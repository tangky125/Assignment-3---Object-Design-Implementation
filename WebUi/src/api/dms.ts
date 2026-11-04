// One function per API endpoint, grouped by business area.
// Screens call these and never build URLs themselves.
import { get, post, query } from './client'
import type {
  AddressInput,
  AddressPair,
  CustomerSummary,
  JobResponse,
  LifecycleTable,
  OperationResult,
  PaymentOutcome,
  QuoteInput,
  QuoteProposal,
  QuoteView,
  RequestDraft,
  RequestSummary,
  SessionUser,
  SignInResponse,
  StaffOption,
  TrackingResponse,
  ValueResult,
} from './types'

const ref = (reference: string): string => encodeURIComponent(reference.trim().toUpperCase())

/** Sign-in, sign-out and reference data. */
export const authApi = {
  signIn: (username: string, password: string): Promise<SignInResponse> =>
    post('/api/auth/sign-in', { username, password }),
  signOut: (): Promise<void> => post('/api/auth/sign-out'),
  me: (): Promise<SessionUser> => get('/api/auth/me'),
  lifecycle: (): Promise<LifecycleTable> => get('/api/lifecycle'),
  demoAccounts: (): Promise<string[]> => get('/api/help/accounts'),
}

/** Area 1: customers and logging requests. */
export const requestApi = {
  findCustomers: (phone: string, name: string): Promise<CustomerSummary[]> =>
    get(`/api/customers${query({ phone, name })}`),
  registerCustomer: (name: string, phone: string, email: string): Promise<ValueResult<CustomerSummary>> =>
    post('/api/customers', { name, phone, email }),
  getCustomer: (id: string): Promise<CustomerSummary> => get(`/api/customers/${encodeURIComponent(id)}`),
  recentAddresses: (id: string): Promise<AddressPair[]> =>
    get(`/api/customers/${encodeURIComponent(id)}/recent-addresses`),
  checkPickup: (address: AddressInput): Promise<OperationResult> =>
    post('/api/requests/check-pickup', address),
  submit: (draft: RequestDraft): Promise<ValueResult<string>> => post('/api/requests', draft),
  cancel: (reference: string, reason: string): Promise<OperationResult> =>
    post(`/api/requests/${ref(reference)}/cancel`, { reason }),
}

/** Area 2: quotes and the customer's decision. */
export const quoteApi = {
  queue: (): Promise<RequestSummary[]> => get('/api/quotes/queue'),
  propose: (reference: string): Promise<ValueResult<QuoteProposal>> =>
    get(`/api/quotes/${ref(reference)}/proposal`),
  issue: (reference: string, input: QuoteInput): Promise<OperationResult> =>
    post(`/api/quotes/${ref(reference)}`, input),
  ownerQueue: (): Promise<RequestSummary[]> => get('/api/quotes/owner-queue'),
  release: (reference: string, price: number): Promise<OperationResult> =>
    post(`/api/quotes/${ref(reference)}/release`, { price }),
  requestsFor: (customerId: string): Promise<RequestSummary[]> =>
    get(`/api/customers/${encodeURIComponent(customerId)}/requests`),
  history: (reference: string): Promise<QuoteView[]> => get(`/api/requests/${ref(reference)}/quotes`),
  /** `confirmedVia` is how the customer gave the decision; required when office staff record it. */
  accept: (reference: string, confirmedVia: string | null = null): Promise<OperationResult> =>
    post(`/api/requests/${ref(reference)}/accept`, { reason: null, confirmedVia }),
  reject: (reference: string, reason: string, confirmedVia: string | null = null): Promise<OperationResult> =>
    post(`/api/requests/${ref(reference)}/reject`, { reason, confirmedVia }),
  revise: (reference: string, reason: string, confirmedVia: string | null = null): Promise<OperationResult> =>
    post(`/api/requests/${ref(reference)}/revise`, { reason, confirmedVia }),
}

/** Area 3: allocate delivery staff. */
export const allocationApi = {
  waiting: (): Promise<RequestSummary[]> => get('/api/allocation/waiting'),
  scheduled: (): Promise<RequestSummary[]> => get('/api/allocation/scheduled'),
  staffOptions: (reference: string): Promise<StaffOption[]> => get(`/api/allocation/${ref(reference)}/staff`),
  allocate: (reference: string, staffId: string): Promise<OperationResult> =>
    post(`/api/allocation/${ref(reference)}`, { staffId }),
  replace: (reference: string, staffId: string, reason: string): Promise<OperationResult> =>
    post(`/api/allocation/${ref(reference)}/replace`, { staffId, reason }),
}

/** Area 4: a delivery staff member's jobs. */
export const jobApi = {
  list: (): Promise<RequestSummary[]> => get('/api/jobs'),
  get: (reference: string): Promise<JobResponse> => get(`/api/jobs/${ref(reference)}`),
  setCollected: (reference: string, parcelId: string, collected: boolean): Promise<OperationResult> =>
    post(`/api/jobs/${ref(reference)}/parcels/${encodeURIComponent(parcelId)}`, { collected }),
  cash: (reference: string, amountReceived: number): Promise<PaymentOutcome> =>
    post(`/api/jobs/${ref(reference)}/payments/cash`, { amountReceived }),
  card: (reference: string, approved: boolean): Promise<PaymentOutcome> =>
    post(`/api/jobs/${ref(reference)}/payments/card`, { approved }),
  confirmCollection: (reference: string): Promise<OperationResult> =>
    post(`/api/jobs/${ref(reference)}/confirm-collection`),
  delivered: (reference: string, receivedBy: string): Promise<OperationResult> =>
    post(`/api/jobs/${ref(reference)}/delivered`, { receivedBy }),
  failure: (reference: string, reason: string): Promise<OperationResult> =>
    post(`/api/jobs/${ref(reference)}/failure`, { reason }),
}

/** Tracking and search. */
export const trackingApi = {
  find: (reference: string): Promise<TrackingResponse> => get(`/api/requests/${ref(reference)}`),
  search: (criteria: {
    reference?: string
    name?: string
    phone?: string
    date?: string
  }): Promise<RequestSummary[]> => get(`/api/search${query(criteria)}`),
  open: (): Promise<RequestSummary[]> => get('/api/open-requests'),
}
