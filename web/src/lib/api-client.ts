const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "";

export type SystemVersion = {
  version: string;
  environment: string;
  releaseChannel: string;
};

export type SetupStatus = {
  setupRequired: boolean;
  challengeExpiresAt: string | null;
  lockedUntil: string | null;
};

export type CurrentAdministrator = {
  username: string;
  sessionStartedAt: string;
  stepUpExpiresAt: string | null;
};

export type SystemSettings = {
  timezone: string;
  releaseChannel: string;
  logLevel: string;
  reportConcurrency: number;
  reportRetentionMonths: number;
  backupRetentionCount: number;
  reportExternalBaseUrl: string | null;
  reportDownloadLinkHours: number;
  reportDownloadMaxDownloads: number | null;
  revision: number;
  updatedAt: string | null;
};

export type Sub2ApiConnection = {
  configured: boolean;
  baseUrl: string | null;
  hasAdminApiKey: boolean;
  adminApiKeyMask: string | null;
  userScopeMode: "SelectedUsers" | "AllActiveUsers";
  codexGroupId: string | null;
  revision: number;
  updatedAt: string | null;
  lastTestedAt: string | null;
  lastTestSucceeded: boolean | null;
  lastTestCode: string | null;
  lastUsersSynchronizedAt: string | null;
  lastSynchronizedUserCount: number | null;
  lastSynchronizedAt: string | null;
  lastSynchronizedKeyCount: number | null;
};

export type Sub2ApiConnectionTest = {
  succeeded: boolean;
  code: string;
  message: string;
  availableUserCount: number | null;
  testedAt: string;
};

export type Sub2ApiUser = {
  id: string;
  externalId: string;
  email: string;
  username: string | null;
  status: string;
  isSelected: boolean;
  lastSeenAt: string;
  retiredAt: string | null;
};

export type Sub2ApiUserScope = {
  scopeMode: "SelectedUsers" | "AllActiveUsers";
  users: Sub2ApiUser[];
  connectionRevision: number;
  lastSynchronizedAt: string | null;
};

export type Sub2ApiUserSynchronization = {
  added: number;
  updated: number;
  retired: number;
  total: number;
  synchronizedAt: string;
  configurationRevision: number;
};

type Sub2ApiUserScopeWire = Omit<
  Sub2ApiUserScope,
  "users" | "lastSynchronizedAt"
> & {
  users: Array<
    Omit<Sub2ApiUser, "username" | "retiredAt"> & {
      username?: string | null;
      retiredAt?: string | null;
    }
  >;
  lastSynchronizedAt?: string | null;
};

export type ApiKeyInventoryItem = {
  id: string;
  externalId: string;
  sourceUserId: string | null;
  sourceUserEmail: string | null;
  name: string;
  status: string;
  groupId: string | null;
  lastUsedAt: string | null;
  lastSeenAt: string;
  retiredAt: string | null;
};

export type ApiKeyInventoryPage = {
  items: ApiKeyInventoryItem[];
  total: number;
  page: number;
  pageSize: number;
  pages: number;
  diagnostics: {
    retiredKeys: number;
  };
  lastSynchronizedAt: string | null;
};

export type KeySynchronization = {
  added: number;
  updated: number;
  retired: number;
  total: number;
  synchronizedAt: string;
  configurationRevision: number;
};

export type ReportStatus = "Complete" | "Partial";
export type ReportTrigger =
  | "ManualDryRun"
  | "Scheduled"
  | "ManualScheduled"
  | "Retry";

export type ReportUsageMetrics = {
  totalRequests: string;
  totalInputTokens: string;
  totalOutputTokens: string;
  totalCacheTokens: string;
  totalCacheCreationTokens: string;
  totalCacheReadTokens: string;
  totalTokens: string;
  totalCost: string;
  totalActualCost: string;
  averageDurationMs: string;
};

export type DayOfWeek =
  | "Sunday"
  | "Monday"
  | "Tuesday"
  | "Wednesday"
  | "Thursday"
  | "Friday"
  | "Saturday";

export type ReportWindowKind =
  | "RollingDays"
  | "PreviousCalendarWeek"
  | "PreviousCalendarMonth"
  | "CustomRange";

export type ReportWindowSpec = {
  key: string;
  kind: ReportWindowKind;
  rollingDays: number | null;
  weekStartsOn: DayOfWeek | null;
  customStartDate: string | null;
  customEndDate: string | null;
};

export const reportWindowKeys = {
  rollingSevenDays: "rolling_7_days",
  rollingThirtyDays: "rolling_30_days",
  previousCalendarWeek: "previous_calendar_week",
  previousCalendarMonth: "previous_calendar_month",
  customRange: "custom_range",
} as const;

export function createDefaultReportWindowSpecs(): ReportWindowSpec[] {
  return [
    {
      key: reportWindowKeys.rollingSevenDays,
      kind: "RollingDays",
      rollingDays: 7,
      weekStartsOn: null,
      customStartDate: null,
      customEndDate: null,
    },
    {
      key: reportWindowKeys.rollingThirtyDays,
      kind: "RollingDays",
      rollingDays: 30,
      weekStartsOn: null,
      customStartDate: null,
      customEndDate: null,
    },
    {
      key: reportWindowKeys.previousCalendarWeek,
      kind: "PreviousCalendarWeek",
      rollingDays: null,
      weekStartsOn: "Monday",
      customStartDate: null,
      customEndDate: null,
    },
    {
      key: reportWindowKeys.previousCalendarMonth,
      kind: "PreviousCalendarMonth",
      rollingDays: null,
      weekStartsOn: null,
      customStartDate: null,
      customEndDate: null,
    },
  ];
}

export type ReportWindowDescriptor = {
  key: string;
  kind: ReportWindowKind;
  rollingDays: number | null;
  weekStartsOn: DayOfWeek | null;
  startDate: string;
  endDateExclusive: string;
  dayCount: number;
  label: string;
};

export type ReportWindowMetrics = {
  windowKey: string;
  metrics: ReportUsageMetrics;
};

export type ReportUserUsage = {
  userId: string;
  externalUserId: number;
  username: string | null;
  email: string;
  keyCount: number;
  windows: ReportWindowMetrics[];
};

export type ReportKeyUsage = {
  keyId: string;
  externalId: string;
  sourceUserId: string | null;
  sourceUserEmail: string | null;
  name: string;
  status: string;
  lastUsedAt: string | null;
  retiredAt: string | null;
  windows: ReportWindowMetrics[];
};

export type ReportRangeFailure = {
  externalUserId: number;
  userEmail: string;
  externalKeyId: number;
  keyName: string;
  windowKey: string;
  startDate: string;
  endDateExclusive: string;
  failureKind: string | null;
  errorCode: string | null;
};

export type ReportDetail = {
  schemaVersion: number;
  reportId: string;
  status: ReportStatus;
  trigger: ReportTrigger;
  generatedAt: string;
  timezone: string;
  connectionRevision: number;
  windows: ReportWindowDescriptor[];
  windowTotals: ReportWindowMetrics[];
  users: ReportUserUsage[];
  keys: ReportKeyUsage[];
  diagnostics: {
    failedRanges: ReportRangeFailure[];
  };
};

export type ReportWindowListSummary = {
  key: string;
  label: string;
  startDate: string;
  endDateExclusive: string;
  dayCount: number;
  totalActualCost: string;
};

export type ReportListItem = {
  id: string;
  schemaVersion: number;
  status: ReportStatus;
  trigger: ReportTrigger;
  cutoffDate: string;
  timezone: string;
  generatedAt: string;
  userCount: number;
  keyCount: number;
  failedRangeCount: number;
  sevenDayActualCost: string;
  thirtyDayActualCost: string;
  windows: ReportWindowListSummary[];
};

export function getReportWindowMetrics(
  metrics: readonly ReportWindowMetrics[],
  windowKey: string,
): ReportUsageMetrics | null {
  return metrics.find((item) => item.windowKey === windowKey)?.metrics ?? null;
}

export type ReportGenerationStatus = "Running" | "Succeeded" | "Failed";

export type ReportGenerationRun = {
  id: string;
  trigger: ReportTrigger;
  status: ReportGenerationStatus;
  stage: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  connectionRevision: number;
  startedAt: string;
  completedAt: string | null;
  reportSnapshotId: string | null;
};

export type ReportGenerationRunPage = {
  items: ReportGenerationRun[];
  total: number;
  page: number;
  pageSize: number;
  pages: number;
};

export type ReportPage = {
  items: ReportListItem[];
  total: number;
  page: number;
  pageSize: number;
  pages: number;
};

type ProblemDetails = {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
};

export class ApiError extends Error {
  readonly status: number;
  readonly errors: Record<string, string[]>;

  constructor(status: number, problem?: ProblemDetails) {
    super(problem?.detail ?? problem?.title ?? `请求失败（${status}）`);
    this.name = "ApiError";
    this.status = status;
    this.errors = problem?.errors ?? {};
  }
}

const antiforgeryExpiredMessage = "Antiforgery token 无效或已过期。";
let antiforgeryToken: string | null = null;

async function getAntiforgeryToken(signal?: AbortSignal): Promise<string> {
  if (antiforgeryToken) {
    return antiforgeryToken;
  }

  const response = await fetch(`${apiBaseUrl}/api/v1/security/antiforgery`, {
    credentials: "same-origin",
    headers: { Accept: "application/json" },
    signal,
  });
  if (!response.ok) {
    throw await createApiError(response);
  }

  const body = (await response.json()) as { token: string };
  antiforgeryToken = body.token;
  return body.token;
}

async function apiRequest<T>(
  path: string,
  options: {
    method?: "GET" | "POST" | "PUT" | "DELETE";
    body?: unknown;
    signal?: AbortSignal;
  } = {},
  retryAntiforgery = true,
): Promise<T> {
  const method = options.method ?? "GET";
  const headers: Record<string, string> = { Accept: "application/json" };
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }
  if (method !== "GET") {
    headers["X-CSRF-TOKEN"] = await getAntiforgeryToken(options.signal);
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    credentials: "same-origin",
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options.signal,
  });
  if (!response.ok) {
    const error = await createApiError(response);
    if (
      method !== "GET" &&
      retryAntiforgery &&
      error.status === 400 &&
      error.message === antiforgeryExpiredMessage
    ) {
      antiforgeryToken = null;
      return apiRequest<T>(path, options, false);
    }
    throw error;
  }
  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

async function createApiError(response: Response): Promise<ApiError> {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("json")) {
    return new ApiError(response.status);
  }

  try {
    return new ApiError(
      response.status,
      (await response.json()) as ProblemDetails,
    );
  } catch {
    return new ApiError(response.status);
  }
}

export function getSystemVersion(signal?: AbortSignal) {
  return apiRequest<SystemVersion>("/api/v1/system/version", { signal });
}

export function getSetupStatus(signal?: AbortSignal) {
  return apiRequest<SetupStatus>("/api/v1/setup/status", { signal });
}

export function initializeAdministrator(input: {
  code: string;
  username: string;
  password: string;
}) {
  return apiRequest<void>("/api/v1/setup/initialize", {
    method: "POST",
    body: input,
  });
}

export function login(input: { username: string; password: string }) {
  return apiRequest<void>("/api/v1/auth/login", {
    method: "POST",
    body: input,
  });
}

export function logout() {
  return apiRequest<void>("/api/v1/auth/logout", { method: "POST" });
}

export function getCurrentAdministrator(signal?: AbortSignal) {
  return apiRequest<CurrentAdministrator>("/api/v1/auth/me", { signal });
}

export function changePassword(input: {
  currentPassword: string;
  newPassword: string;
}) {
  return apiRequest<void>("/api/v1/auth/change-password", {
    method: "POST",
    body: input,
  });
}

export function createStepUp(input: { password: string }) {
  return apiRequest<CurrentAdministrator>("/api/v1/auth/step-up", {
    method: "POST",
    body: input,
  });
}

export function recoverAdministrator(input: {
  username: string;
  code: string;
  newPassword: string;
}) {
  return apiRequest<void>("/api/v1/auth/recover", {
    method: "POST",
    body: input,
  });
}

export function getSystemSettings(signal?: AbortSignal) {
  return apiRequest<SystemSettings>("/api/v1/system/settings", { signal });
}

export function updateSystemSettings(input: Omit<SystemSettings, "updatedAt">) {
  return apiRequest<SystemSettings>("/api/v1/system/settings", {
    method: "PUT",
    body: input,
  });
}

export function getSub2ApiConnection(signal?: AbortSignal) {
  return apiRequest<Sub2ApiConnection>("/api/v1/sub2api/connection", {
    signal,
  });
}

export function saveSub2ApiConnection(input: {
  baseUrl: string;
  adminApiKey: string | null;
  clearAdminApiKey: boolean;
  codexGroupId: string | null;
  revision: number;
}) {
  return apiRequest<Sub2ApiConnection>("/api/v1/sub2api/connection", {
    method: "PUT",
    body: input,
  });
}

export function testSub2ApiConnection() {
  return apiRequest<Sub2ApiConnectionTest>("/api/v1/sub2api/connection/test", {
    method: "POST",
  });
}

function normalizeSub2ApiUserScope(
  scope: Sub2ApiUserScopeWire,
): Sub2ApiUserScope {
  return {
    ...scope,
    lastSynchronizedAt: scope.lastSynchronizedAt ?? null,
    users: scope.users.map((user) => ({
      ...user,
      username: user.username ?? null,
      retiredAt: user.retiredAt ?? null,
    })),
  };
}

export async function getSub2ApiUsers(signal?: AbortSignal) {
  const scope = await apiRequest<Sub2ApiUserScopeWire>(
    "/api/v1/sub2api/users",
    { signal },
  );
  return normalizeSub2ApiUserScope(scope);
}

export function synchronizeSub2ApiUsers() {
  return apiRequest<Sub2ApiUserSynchronization>("/api/v1/sub2api/users/sync", {
    method: "POST",
  });
}

export async function updateSub2ApiUserScope(input: {
  mode: "SelectedUsers" | "AllActiveUsers";
  selectedUserIds: string[];
  revision: number;
}) {
  const scope = await apiRequest<Sub2ApiUserScopeWire>(
    "/api/v1/sub2api/users/scope",
    {
      method: "PUT",
      body: input,
    },
  );
  return normalizeSub2ApiUserScope(scope);
}

export function synchronizeSub2ApiKeys() {
  return apiRequest<KeySynchronization>("/api/v1/sub2api/keys/sync", {
    method: "POST",
  });
}

export function getApiKeyInventory(
  page: number,
  retiredOnly: boolean,
  signal?: AbortSignal,
) {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: "50",
    retiredOnly: String(retiredOnly),
  });
  return apiRequest<ApiKeyInventoryPage>(`/api/v1/sub2api/keys?${query}`, {
    signal,
  });
}

export function getReports(page: number, signal?: AbortSignal) {
  const query = new URLSearchParams({ page: String(page), pageSize: "25" });
  return apiRequest<ReportPage>(`/api/v1/reports?${query}`, { signal });
}

export function getReport(id: string, signal?: AbortSignal) {
  return apiRequest<ReportDetail>(`/api/v1/reports/${encodeURIComponent(id)}`, {
    signal,
  });
}

export function generateReport(
  cutoffDate: string | null,
  windows: ReportWindowSpec[] | null,
) {
  return apiRequest<ReportDetail>("/api/v1/reports/dry-run", {
    method: "POST",
    body: { cutoffDate, windows },
  });
}

export function getReportGenerationRuns(page: number, signal?: AbortSignal) {
  const query = new URLSearchParams({ page: String(page), pageSize: "20" });
  return apiRequest<ReportGenerationRunPage>(
    `/api/v1/reports/generations?${query}`,
    { signal },
  );
}

export type ShortMonthStrategy = "UseLastDay" | "SkipMonth";

export type ReportSchedule = {
  enabled: boolean;
  dayOfMonth: number;
  shortMonthStrategy: ShortMonthStrategy;
  localTime: string;
  timezone: string;
  windows: ReportWindowSpec[];
  revision: number;
  updatedAt: string | null;
  nextRunAt: string | null;
  synchronized: boolean;
  synchronizationErrorCode: string | null;
};

export type ReportTaskTrigger =
  | "Scheduled"
  | "ManualScheduled"
  | "Retry";
export type ReportTaskStatus =
  | "Queued"
  | "Collecting"
  | "Rendering"
  | "Delivering"
  | "Succeeded"
  | "PartialFailed"
  | "Failed";

export type ReportTaskRun = {
  id: string;
  trigger: ReportTaskTrigger;
  status: ReportTaskStatus;
  reportId: string | null;
  periodEnd: string | null;
  timezone: string | null;
  scheduleRevision: number | null;
  retryOfRunId: string | null;
  attempt: number;
  startedAt: string;
  collectingAt: string | null;
  renderingAt: string | null;
  deliveringAt: string | null;
  completedAt: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  deliveryCount: number;
  succeededDeliveryCount: number;
  failedDeliveryCount: number;
  hasOutcomeUnknown: boolean;
  canRetry: boolean;
};

export type ReportTaskRunPage = {
  items: ReportTaskRun[];
  total: number;
  page: number;
  pageSize: number;
  pages: number;
};

export function getReportSchedule(signal?: AbortSignal) {
  return apiRequest<ReportSchedule>("/api/v1/schedule", { signal });
}

export function updateReportSchedule(input: {
  enabled: boolean;
  dayOfMonth: number;
  shortMonthStrategy?: ShortMonthStrategy;
  localTime: string;
  timezone: string;
  windows: ReportWindowSpec[];
  revision: number;
}) {
  return apiRequest<ReportSchedule>("/api/v1/schedule", {
    method: "PUT",
    body: input,
  });
}

export function runReportScheduleNow() {
  return apiRequest<ReportTaskRun>("/api/v1/schedule/run", {
    method: "POST",
  });
}

export function getReportTaskRuns(page: number, signal?: AbortSignal) {
  const query = new URLSearchParams({ page: String(page), pageSize: "20" });
  return apiRequest<ReportTaskRunPage>(`/api/v1/schedule/runs?${query}`, {
    signal,
  });
}

export function retryReportTaskRun(
  runId: string,
  confirmOutcomeUnknown: boolean,
) {
  return apiRequest<ReportTaskRun>(
    `/api/v1/schedule/runs/${encodeURIComponent(runId)}/retry`,
    {
      method: "POST",
      body: { confirmOutcomeUnknown },
    },
  );
}

export async function downloadReportXlsx(id: string, signal?: AbortSignal) {
  const response = await fetch(
    `${apiBaseUrl}/api/v1/reports/${encodeURIComponent(id)}/xlsx`,
    {
      credentials: "same-origin",
      headers: {
        Accept: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      },
      signal,
    },
  );
  if (!response.ok) {
    throw await createApiError(response);
  }

  const disposition = response.headers.get("content-disposition") ?? "";
  const encodedName = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
  return {
    blob: await response.blob(),
    fileName: encodedName
      ? decodeURIComponent(encodedName)
      : `sub2api-report-${id}.xlsx`,
  };
}

export type NotificationChannelType = "Email" | "DingTalk" | "Feishu";
export type SmtpSecurityMode = "StartTls" | "ImplicitTls" | "None";

export type EmailChannelDisplay = {
  host: string;
  port: number;
  security: SmtpSecurityMode;
  username: string | null;
  fromAddress: string;
  fromName: string | null;
  toAddresses: string[];
  ccAddresses: string[];
  hasPassword: boolean;
  passwordMask: string | null;
};

export type WebhookChannelDisplay = {
  hasWebhook: boolean;
  webhookMask: string | null;
  signSecretMask: string | null;
};

export type NotificationChannel = {
  id: string;
  type: NotificationChannelType;
  name: string;
  enabled: boolean;
  email: EmailChannelDisplay | null;
  webhook: WebhookChannelDisplay | null;
  revision: number;
  createdAt: string;
  updatedAt: string;
  lastTestedAt: string | null;
  lastTestSucceeded: boolean | null;
  lastTestCode: string | null;
};

export type ChannelTest = {
  succeeded: boolean;
  code: string;
  message: string;
  testedAt: string;
};

export type EmailChannelInput = {
  host: string;
  port: number;
  security: SmtpSecurityMode;
  username: string | null;
  fromAddress: string;
  fromName: string | null;
  toAddresses: string[];
  ccAddresses: string[];
};

export type CreateChannelInput = {
  type: NotificationChannelType;
  name: string;
  enabled: boolean;
  email: EmailChannelInput | null;
  smtpPassword: string | null;
  webhookUrl: string | null;
  signSecret: string | null;
};

export type UpdateChannelInput = {
  name: string;
  enabled: boolean;
  email: EmailChannelInput | null;
  removeStoredPassword: boolean;
  newSmtpPassword: string | null;
  webhookUrl: string | null;
  signSecret: string | null;
  revision: number;
};

export type DeliveryPart = {
  index: number;
  count: number;
  status: "Pending" | "Succeeded" | "Failed";
  attempts: number;
  errorCode: string | null;
  sentAt: string | null;
};

export type DeliveryStatus = "Pending" | "Sending" | "Succeeded" | "Failed";
export type DeliveryRunStatus =
  | "Running"
  | "Succeeded"
  | "PartialFailed"
  | "Failed";


export type ReportDownloadGrant = {
  id: string;
  expiresAt: string | null;
  revokedAt: string | null;
  downloadCount: number;
  maxDownloads: number | null;
  lastDownloadedAt: string | null;
};
export type Delivery = {
  id: string;
  channelId: string;
  channelType: NotificationChannelType;
  channelName: string;
  status: DeliveryStatus;
  attempts: number;
  errorCode: string | null;
  errorMessage: string | null;
  sentAt: string | null;
  parts: DeliveryPart[];
  downloadGrant: ReportDownloadGrant | null;
};

export type DeliveryRun = {
  id: string;
  reportId: string;
  status: DeliveryRunStatus;
  startedAt: string;
  completedAt: string | null;
  deliveries: Delivery[];
};

export function getChannels(signal?: AbortSignal) {
  return apiRequest<NotificationChannel[]>("/api/v1/channels", { signal });
}

export function createChannel(input: CreateChannelInput) {
  return apiRequest<NotificationChannel>("/api/v1/channels", {
    method: "POST",
    body: input,
  });
}

export function updateChannel(id: string, input: UpdateChannelInput) {
  return apiRequest<NotificationChannel>(
    `/api/v1/channels/${encodeURIComponent(id)}`,
    {
      method: "PUT",
      body: input,
    },
  );
}

export function deleteChannel(id: string) {
  return apiRequest<void>(`/api/v1/channels/${encodeURIComponent(id)}`, {
    method: "DELETE",
  });
}

export function testChannel(id: string) {
  return apiRequest<ChannelTest>(
    `/api/v1/channels/${encodeURIComponent(id)}/test`,
    { method: "POST" },
  );
}

export function getReportDeliveries(reportId: string, signal?: AbortSignal) {
  return apiRequest<DeliveryRun[]>(
    `/api/v1/reports/${encodeURIComponent(reportId)}/deliveries`,
    { signal },
  );
}

export function deliverReport(
  reportId: string,
  input: { channelIds: string[]; confirmPartial: boolean },
) {
  return apiRequest<DeliveryRun>(
    `/api/v1/reports/${encodeURIComponent(reportId)}/deliveries`,
    {
      method: "POST",
      body: input,
    },
  );
}

export function retryReportDelivery(reportId: string, runId: string) {
  return apiRequest<DeliveryRun>(
    `/api/v1/reports/${encodeURIComponent(reportId)}/deliveries/${encodeURIComponent(runId)}/retry`,
    { method: "POST" },
  );
}

export function revokeReportDownloadGrant(reportId: string, grantId: string) {
  return apiRequest<void>(
    `/api/v1/reports/${encodeURIComponent(reportId)}/download-grants/${encodeURIComponent(grantId)}/revoke`,
    { method: "POST" },
  );
}

export type UpdaterStatus = {
  version: string;
  installationEnabled: boolean;
  state: string;
  lastCheckedAt: string | null;
  availableVersion: string | null;
  lastOperationId: string | null;
  lastOperationState: string | null;
};

export type UpdateCheck = {
  updateAvailable: boolean;
  currentVersion: string;
  availableVersion: string | null;
  publishedAt: string | null;
  manualUpgradeRequired: boolean;
  upgradeMessage: string;
};

export type UpdatePlanStep = {
  order: number;
  name: string;
  description: string;
};

export type UpdatePlan = {
  currentVersion: string;
  targetVersion: string | null;
  installationEnabled: boolean;
  manualUpgradeRequired: boolean;
  upgradeMessage: string;
  steps: UpdatePlanStep[];
};

export type UpdateStage = {
  stage: string;
  startedAt: string;
  completedAt: string | null;
  error: string | null;
};

export type UpdateOperation = {
  operationId: string;
  state: string;
  stage: string | null;
  currentVersion: string;
  targetVersion: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
  lastError: string | null;
  stages: UpdateStage[];
};

export type InstallUpdateAccepted = {
  operationId: string;
  state: string;
};

export function getUpdateStatus(signal?: AbortSignal) {
  return apiRequest<UpdaterStatus>("/api/v1/updates/status", { signal });
}

export function checkForUpdates() {
  return apiRequest<UpdateCheck>("/api/v1/updates/check", { method: "POST" });
}

export function getUpdatePlan(signal?: AbortSignal) {
  return apiRequest<UpdatePlan>("/api/v1/updates/plan", { signal });
}

export function installUpdate(targetVersion: string | null) {
  return apiRequest<InstallUpdateAccepted>("/api/v1/updates/install", {
    method: "POST",
    body: { confirm: true, targetVersion },
  });
}

export function getUpdateOperation(operationId: string, signal?: AbortSignal) {
  return apiRequest<UpdateOperation>(
    `/api/v1/updates/operations/${encodeURIComponent(operationId)}`,
    { signal },
  );
}
