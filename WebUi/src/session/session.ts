// Who is signed in, kept in one reactive store shared by every screen.
// The sign-in token lives in sessionStorage, so a page refresh keeps the user signed in
// but closing the tab signs them out.
import { computed, reactive } from 'vue'
import { authApi } from '@/api/dms'
import { configureClient } from '@/api/client'
import type { LifecycleTable, SessionUser, Status, UserRole } from '@/api/types'

const TokenKey = 'dms.token'

function readToken(): string | null {
  try {
    return sessionStorage.getItem(TokenKey)
  } catch {
    return null
  }
}

function writeToken(token: string | null): void {
  try {
    if (token === null) {
      sessionStorage.removeItem(TokenKey)
    } else {
      sessionStorage.setItem(TokenKey, token)
    }
  } catch {
    // Storage can be blocked (private mode); the session then lasts until the page is reloaded.
  }
}

interface SessionState {
  token: string | null
  user: SessionUser | null
  lifecycle: LifecycleTable | null
  expired: boolean
}

export const session = reactive<SessionState>({
  token: readToken(),
  user: null,
  lifecycle: null,
  expired: false,
})

configureClient(
  () => session.token,
  () => {
    if (session.user !== null) {
      session.expired = true
    }
    clearSession()
  },
)

function clearSession(): void {
  session.token = null
  session.user = null
  writeToken(null)
}

/** Readable name of a role. */
export function roleText(role: UserRole): string {
  return {
    Customer: 'Customer',
    OfficeStaff: 'Office staff',
    DeliveryStaff: 'Delivery staff',
    Owner: 'Owner',
  }[role]
}

/** True when the signed-in user holds any of the roles. */
export function hasRole(...roles: UserRole[]): boolean {
  return session.user !== null && roles.some((r) => session.user!.roles.includes(r))
}

/** Office work is done by office staff and by owners. */
export const isOffice = computed(() => hasRole('OfficeStaff', 'Owner'))

/** Any staff role. */
export const isStaff = computed(() => hasRole('OfficeStaff', 'Owner', 'DeliveryStaff'))

/** Signs in; throws ApiRequestError with the server's message when the details are wrong. */
export async function signIn(username: string, password: string): Promise<SessionUser> {
  const response = await authApi.signIn(username, password)
  session.token = response.token
  session.user = response.user
  session.expired = false
  writeToken(response.token)
  return response.user
}

/** Signs out on the server and forgets the session here. */
export async function signOut(): Promise<void> {
  try {
    await authApi.signOut()
  } finally {
    clearSession()
  }
}

/** Reloads the signed-in user after a page refresh, and the status rules. Safe to call more than once. */
export async function restoreSession(): Promise<void> {
  if (session.lifecycle === null) {
    session.lifecycle = await authApi.lifecycle()
  }
  if (session.token !== null && session.user === null) {
    try {
      session.user = await authApi.me()
    } catch {
      clearSession()
    }
  }
}

/** Asks the server's StatusLifecycle table whether a status change is legal. */
export function canTransition(from: Status, to: Status): boolean {
  return session.lifecycle?.[from]?.includes(to) ?? false
}
