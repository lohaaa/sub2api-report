const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export type SystemVersion = {
  version: string
  environment: string
  releaseChannel: string
}

export type SetupStatus = {
  setupRequired: boolean
  challengeExpiresAt: string | null
  lockedUntil: string | null
}

export type CurrentAdministrator = {
  username: string
  sessionStartedAt: string
  stepUpExpiresAt: string | null
}

export type SystemSettings = {
  timezone: string
  releaseChannel: string
  logLevel: string
  reportRetentionMonths: number
  backupRetentionCount: number
  revision: number
  updatedAt: string | null
}

export type Sub2ApiConnection = {
  configured: boolean
  baseUrl: string | null
  hasAdminApiKey: boolean
  adminApiKeyMask: string | null
  userId: string | null
  codexGroupId: string | null
  revision: number
  updatedAt: string | null
  lastTestedAt: string | null
  lastTestSucceeded: boolean | null
  lastTestCode: string | null
  lastSynchronizedAt: string | null
  lastSynchronizedKeyCount: number | null
}

export type Sub2ApiConnectionTest = {
  succeeded: boolean
  code: string
  message: string
  availableKeyCount: number | null
  testedAt: string
}

export type Person = {
  id: string
  code: string
  displayName: string
  isActive: boolean
  currentApiKeyCount: number
  assignmentCount: number
  revision: number
  updatedAt: string
}

export type ApiKeyAssignment = {
  id: string
  personId: string
  personCode: string
  personDisplayName: string
  validFrom: string
  validTo: string | null
  revision: number
}

export type ApiKeyInventoryItem = {
  id: string
  externalId: string
  name: string
  status: string
  groupId: string | null
  lastUsedAt: string | null
  lastSeenAt: string
  retiredAt: string | null
  assignments: ApiKeyAssignment[]
}

export type ApiKeyInventoryPage = {
  items: ApiKeyInventoryItem[]
  total: number
  page: number
  pageSize: number
  pages: number
  diagnostics: {
    unmappedKeys: number
    overlappingAssignments: number
    retiredKeys: number
  }
  lastSynchronizedAt: string | null
}

export type KeySynchronization = {
  added: number
  updated: number
  retired: number
  total: number
  synchronizedAt: string
  configurationRevision: number
}

type ProblemDetails = {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly errors: Record<string, string[]>

  constructor(status: number, problem?: ProblemDetails) {
    super(problem?.detail ?? problem?.title ?? `请求失败（${status}）`)
    this.name = 'ApiError'
    this.status = status
    this.errors = problem?.errors ?? {}
  }
}

let antiforgeryToken: string | null = null

async function getAntiforgeryToken(signal?: AbortSignal): Promise<string> {
  if (antiforgeryToken) {
    return antiforgeryToken
  }

  const response = await fetch(`${apiBaseUrl}/api/v1/security/antiforgery`, {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
    signal,
  })
  if (!response.ok) {
    throw await createApiError(response)
  }

  const body = await response.json() as { token: string }
  antiforgeryToken = body.token
  return body.token
}

async function apiRequest<T>(
  path: string,
  options: {
    method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
    body?: unknown
    signal?: AbortSignal
  } = {},
): Promise<T> {
  const method = options.method ?? 'GET'
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  if (method !== 'GET') {
    headers['X-CSRF-TOKEN'] = await getAntiforgeryToken(options.signal)
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    credentials: 'same-origin',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options.signal,
  })
  if (!response.ok) {
    throw await createApiError(response)
  }
  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

async function createApiError(response: Response): Promise<ApiError> {
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('json')) {
    return new ApiError(response.status)
  }

  try {
    return new ApiError(response.status, await response.json() as ProblemDetails)
  }
  catch {
    return new ApiError(response.status)
  }
}

export function getSystemVersion(signal?: AbortSignal) {
  return apiRequest<SystemVersion>('/api/v1/system/version', { signal })
}

export function getSetupStatus(signal?: AbortSignal) {
  return apiRequest<SetupStatus>('/api/v1/setup/status', { signal })
}

export function initializeAdministrator(input: { code: string; username: string; password: string }) {
  return apiRequest<void>('/api/v1/setup/initialize', { method: 'POST', body: input })
}

export function login(input: { username: string; password: string }) {
  return apiRequest<void>('/api/v1/auth/login', { method: 'POST', body: input })
}

export function logout() {
  return apiRequest<void>('/api/v1/auth/logout', { method: 'POST' })
}

export function getCurrentAdministrator(signal?: AbortSignal) {
  return apiRequest<CurrentAdministrator>('/api/v1/auth/me', { signal })
}

export function changePassword(input: { currentPassword: string; newPassword: string }) {
  return apiRequest<void>('/api/v1/auth/change-password', { method: 'POST', body: input })
}

export function createStepUp(input: { password: string }) {
  return apiRequest<CurrentAdministrator>('/api/v1/auth/step-up', { method: 'POST', body: input })
}

export function recoverAdministrator(input: { username: string; code: string; newPassword: string }) {
  return apiRequest<void>('/api/v1/auth/recover', { method: 'POST', body: input })
}

export function getSystemSettings(signal?: AbortSignal) {
  return apiRequest<SystemSettings>('/api/v1/system/settings', { signal })
}

export function updateSystemSettings(input: Omit<SystemSettings, 'updatedAt'>) {
  return apiRequest<SystemSettings>('/api/v1/system/settings', { method: 'PUT', body: input })
}

export function getSub2ApiConnection(signal?: AbortSignal) {
  return apiRequest<Sub2ApiConnection>('/api/v1/sub2api/connection', { signal })
}

export function saveSub2ApiConnection(input: {
  baseUrl: string
  adminApiKey: string | null
  clearAdminApiKey: boolean
  userId: string
  codexGroupId: string | null
  revision: number
}) {
  return apiRequest<Sub2ApiConnection>('/api/v1/sub2api/connection', { method: 'PUT', body: input })
}

export function testSub2ApiConnection() {
  return apiRequest<Sub2ApiConnectionTest>('/api/v1/sub2api/connection/test', { method: 'POST' })
}

export function synchronizeSub2ApiKeys() {
  return apiRequest<KeySynchronization>('/api/v1/sub2api/keys/sync', { method: 'POST' })
}

export function getApiKeyInventory(page: number, unmappedOnly: boolean, signal?: AbortSignal) {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: '50',
    unmappedOnly: String(unmappedOnly),
  })
  return apiRequest<ApiKeyInventoryPage>(`/api/v1/sub2api/keys?${query}`, { signal })
}

export function getPeople(signal?: AbortSignal) {
  return apiRequest<Person[]>('/api/v1/people', { signal })
}

export function createPerson(input: { code: string; displayName: string }) {
  return apiRequest<Person>('/api/v1/people', { method: 'POST', body: input })
}

export function updatePerson(id: string, input: {
  code: string
  displayName: string
  isActive: boolean
  revision: number
}) {
  return apiRequest<Person>(`/api/v1/people/${id}`, { method: 'PUT', body: input })
}

export function deactivatePerson(id: string) {
  return apiRequest<void>(`/api/v1/people/${id}`, { method: 'DELETE' })
}

export function createApiKeyAssignment(personId: string, input: {
  externalApiKeyId: string
  validFrom: string
  validTo: string | null
}) {
  return apiRequest<ApiKeyAssignment>(`/api/v1/people/${personId}/assignments`, {
    method: 'POST',
    body: input,
  })
}

export function updateApiKeyAssignment(id: string, input: {
  validFrom: string
  validTo: string | null
  revision: number
}) {
  return apiRequest<ApiKeyAssignment>(`/api/v1/people/assignments/${id}`, {
    method: 'PUT',
    body: input,
  })
}

export function deleteApiKeyAssignment(id: string) {
  return apiRequest<void>(`/api/v1/people/assignments/${id}`, { method: 'DELETE' })
}
