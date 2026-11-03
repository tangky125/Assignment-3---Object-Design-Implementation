// A small typed wrapper around fetch for the DMS web API (served by program.cs).
// It adds the sign-in token to every request and turns error responses into ApiRequestError
// carrying the server's plain-language message, so screens can show it as is.

/** Thrown when the server refuses a request (400/401/403/404) or cannot be reached. */
export class ApiRequestError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiRequestError'
    this.status = status
  }
}

type TokenSource = () => string | null
type UnauthorisedHandler = () => void

let tokenSource: TokenSource = () => null
let onUnauthorised: UnauthorisedHandler = () => {}

/** Connects the client to the session store (called once at start-up). */
export function configureClient(getToken: TokenSource, handleUnauthorised: UnauthorisedHandler): void {
  tokenSource = getToken
  onUnauthorised = handleUnauthorised
}

async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const token = tokenSource()
  if (token) {
    headers.Authorization = `Bearer ${token}`
  }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }

  let response: Response
  try {
    response = await fetch(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiRequestError(
      'The Delivery Management System server cannot be reached. Check that it is running and try again.',
      0,
    )
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  const data: unknown = text.length > 0 ? JSON.parse(text) : undefined
  if (!response.ok) {
    if (response.status === 401 && path !== '/api/auth/sign-in') {
      onUnauthorised()
    }
    const message =
      typeof data === 'object' && data !== null && 'message' in data
        ? String((data as { message: unknown }).message)
        : `The request failed (${response.status}). Please try again.`
    throw new ApiRequestError(message, response.status)
  }
  return data as T
}

/** GET a JSON resource. */
export function get<T>(path: string): Promise<T> {
  return send<T>('GET', path)
}

/** POST a JSON body and read the JSON answer. */
export function post<T>(path: string, body?: unknown): Promise<T> {
  return send<T>('POST', path, body ?? {})
}

/** Builds a query string, leaving out empty values. */
export function query(params: Record<string, string | null | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value.trim() !== '') {
      search.set(key, value.trim())
    }
  }
  const text = search.toString()
  return text.length > 0 ? `?${text}` : ''
}
