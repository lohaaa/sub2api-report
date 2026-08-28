import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ThemeProvider } from "@/app/theme-provider";
import App from "@/App";
import { updateSystemSettings } from "@/lib/api-client";
import { server } from "./setup";

function renderApp(route = "/") {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <TooltipProvider>
          <MemoryRouter initialEntries={[route]}>
            <App />
          </MemoryRouter>
        </TooltipProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  );
}

function useAuthenticatedHandlers() {
  server.use(
    http.get("/api/v1/setup/status", () =>
      HttpResponse.json({ setupRequired: false }),
    ),
    http.get("/api/v1/auth/me", () =>
      HttpResponse.json({
        username: "synthetic-admin",
        sessionStartedAt: "2026-08-26T10:00:00Z",
        stepUpExpiresAt: null,
      }),
    ),
    http.get("/api/v1/system/version", () =>
      HttpResponse.json({
        version: "0.7.0",
        environment: "Test",
        releaseChannel: "stable",
      }),
    ),
    http.get("/api/v1/schedule", () =>
      HttpResponse.json({
        enabled: false,
        dayOfMonth: 1,
        localTime: "09:00",
        timezone: "Asia/Shanghai",
        windows: defaultScheduleWindows,
        revision: 1,
        updatedAt: null,
        nextRunAt: null,
        synchronized: true,
        synchronizationErrorCode: null,
      }),
    ),
    http.get("/api/v1/channels", () => HttpResponse.json([])),
    http.get("/api/v1/reports/:id/deliveries", () => HttpResponse.json([])),
  );
}

const defaultScheduleWindows = [
  {
    key: "rolling_7_days",
    kind: "RollingDays",
    rollingDays: 7,
    weekStartsOn: null,
    customStartDate: null,
    customEndDate: null,
  },
  {
    key: "rolling_30_days",
    kind: "RollingDays",
    rollingDays: 30,
    weekStartsOn: null,
    customStartDate: null,
    customEndDate: null,
  },
  {
    key: "previous_calendar_week",
    kind: "PreviousCalendarWeek",
    rollingDays: null,
    weekStartsOn: "Monday",
    customStartDate: null,
    customEndDate: null,
  },
  {
    key: "previous_calendar_month",
    kind: "PreviousCalendarMonth",
    rollingDays: null,
    weekStartsOn: null,
    customStartDate: null,
    customEndDate: null,
  },
];

function windowMetrics(windowKey: string, requests: string, tokens: string, cost: string) {
  return {
    windowKey,
    metrics: {
      totalRequests: requests,
      totalInputTokens: "0",
      totalOutputTokens: "0",
      totalCacheTokens: "0",
      totalCacheCreationTokens: "0",
      totalCacheReadTokens: "0",
      totalTokens: tokens,
      totalCost: cost,
      totalActualCost: cost,
      averageDurationMs: "0",
    },
  };
}

const dynamicReportDetail = {
  schemaVersion: 4,
  reportId: "11111111-1111-1111-1111-111111111111",
  status: "Complete",
  trigger: "ManualDryRun",
  generatedAt: "2026-08-27T10:04:00Z",
  timezone: "Asia/Shanghai",
  connectionRevision: 2,
  windows: [
    {
      key: "rolling_7_days",
      kind: "RollingDays",
      rollingDays: 7,
      weekStartsOn: null,
      startDate: "2026-08-20",
      endDateExclusive: "2026-08-27",
      dayCount: 7,
      label: "最近 7 天",
    },
    {
      key: "previous_calendar_week",
      kind: "PreviousCalendarWeek",
      rollingDays: null,
      weekStartsOn: "Monday",
      startDate: "2026-08-17",
      endDateExclusive: "2026-08-24",
      dayCount: 7,
      label: "上一完整自然周",
    },
  ],
  windowTotals: [
    windowMetrics("rolling_7_days", "12", "480", "1.25"),
    windowMetrics("previous_calendar_week", "30", "1200", "3.5"),
  ],
  users: [
    {
      userId: "88888888-8888-8888-8888-888888888888",
      externalUserId: 42,
      username: null,
      email: "synthetic.user@example.com",
      keyCount: 2,
      windows: [
        windowMetrics("rolling_7_days", "12", "480", "1.25"),
        windowMetrics("previous_calendar_week", "30", "1200", "3.5"),
      ],
    },
  ],
  keys: [
    {
      keyId: "99999999-9999-9999-9999-999999999999",
      externalId: "101",
      sourceUserId: "42",
      sourceUserEmail: "synthetic.user@example.com",
      name: "合成 Key",
      status: "active",
      lastUsedAt: null,
      retiredAt: null,
      windows: [
        windowMetrics("rolling_7_days", "12", "480", "1.25"),
        windowMetrics("previous_calendar_week", "30", "1200", "3.5"),
      ],
    },
  ],
  diagnostics: {
    failedRanges: [
      {
        externalUserId: 42,
        userEmail: "synthetic.user@example.com",
        externalKeyId: 101,
        keyName: "合成 Key",
        windowKey: "previous_calendar_week",
        startDate: "2026-08-17",
        endDateExclusive: "2026-08-24",
        failureKind: "UpstreamError",
        errorCode: "E502",
      },
    ],
  },
};

describe("application authentication gate", () => {
  it("renders setup before an administrator exists", async () => {
    server.use(
      http.get("/api/v1/setup/status", () =>
        HttpResponse.json({
          setupRequired: true,
          challengeExpiresAt: "2026-08-26T10:30:00Z",
        }),
      ),
    );

    renderApp("/");

    expect(
      await screen.findByRole("heading", { level: 1, name: "初始化管理员" }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("初始化码")).toHaveAttribute(
      "autocomplete",
      "one-time-code",
    );
    expect(screen.getByLabelText("管理员密码")).toHaveAttribute(
      "autocomplete",
      "new-password",
    );
  });

  it("renders login for an unauthenticated initialized instance", async () => {
    server.use(
      http.get("/api/v1/setup/status", () =>
        HttpResponse.json({ setupRequired: false }),
      ),
      http.get("/api/v1/auth/me", () =>
        HttpResponse.json(
          { title: "Unauthorized", status: 401 },
          { status: 401 },
        ),
      ),
    );

    renderApp("/");

    expect(
      await screen.findByRole("heading", { level: 1, name: "管理员登录" }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("用户名")).toHaveAttribute(
      "autocomplete",
      "username",
    );
    expect(screen.getByLabelText("密码")).toHaveAttribute(
      "autocomplete",
      "current-password",
    );
  });

  it("renders live dashboard status after authentication", async () => {
    useAuthenticatedHandlers();
    server.use(
      http.get("/api/v1/sub2api/connection", () =>
        HttpResponse.json({
          configured: true,
          baseUrl: "https://sub2api.example.com",
          hasAdminApiKey: true,
          adminApiKeyMask: "****1234",
          userScopeMode: "SelectedUsers",
          codexGroupId: null,
          revision: 2,
          updatedAt: "2026-08-27T10:00:00Z",
          lastTestedAt: "2026-08-27T10:01:00Z",
          lastTestSucceeded: true,
          lastTestCode: "connected",
          lastUsersSynchronizedAt: "2026-08-27T10:02:00Z",
          lastSynchronizedUserCount: 1,
          lastSynchronizedAt: "2026-08-27T10:03:00Z",
          lastSynchronizedKeyCount: 17,
        }),
      ),
      http.get("/api/v1/sub2api/keys", () =>
        HttpResponse.json({
          items: [],
          total: 17,
          page: 1,
          pageSize: 50,
          pages: 1,
          diagnostics: { retiredKeys: 0 },
          lastSynchronizedAt: "2026-08-27T10:03:00Z",
        }),
      ),
      http.get("/api/v1/reports", () =>
        HttpResponse.json({
          items: [
            {
              id: "11111111-1111-1111-1111-111111111111",
              schemaVersion: 3,
              status: "Complete",
              trigger: "ManualDryRun",
              cutoffDate: "2026-08-25",
              timezone: "Asia/Shanghai",
              generatedAt: "2026-08-27T10:04:00Z",
              userCount: 1,
              keyCount: 17,
              failedRangeCount: 0,
              sevenDayActualCost: "1.25",
              thirtyDayActualCost: "3.25",
              windows: [
                {
                  key: "rolling_30_days",
                  label: "最近 30 天",
                  startDate: "2026-07-27",
                  endDateExclusive: "2026-08-26",
                  dayCount: 30,
                  totalActualCost: "42.5",
                },
              ],
            },
          ],
          total: 1,
          page: 1,
          pageSize: 25,
          pages: 1,
        }),
      ),
      http.get("/api/v1/reports/generations", () =>
        HttpResponse.json({
          items: [
            {
              id: "22222222-2222-2222-2222-222222222222",
              trigger: "ManualDryRun",
              status: "Succeeded",
              stage: null,
              errorCode: null,
              errorMessage: null,
              connectionRevision: 2,
              startedAt: "2026-08-27T10:03:00Z",
              completedAt: "2026-08-27T10:04:00Z",
              reportSnapshotId: "11111111-1111-1111-1111-111111111111",
            },
          ],
          total: 1,
          page: 1,
          pageSize: 20,
          pages: 1,
        }),
      ),
      http.get("/api/v1/channels", () =>
        HttpResponse.json([
          {
            id: "33333333-3333-3333-3333-333333333333",
            type: "Email",
            name: "合成渠道",
            enabled: true,
          },
        ]),
      ),
    );

    renderApp();

    expect(
      await screen.findByRole("heading", { level: 1, name: "工作台" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("v0.7.0")).toBeInTheDocument();
    expect(await screen.findByText("已配置")).toBeInTheDocument();
    expect(screen.getByText("1 个用户")).toBeInTheDocument();
    expect(await screen.findByText("17 个 Key")).toBeInTheDocument();
    expect(screen.getByText("最近报告费用（USD）")).toBeInTheDocument();
    expect((await screen.findAllByText("42.50")).length).toBeGreaterThan(0);
    expect(screen.getByText("滚动 30 天费用（USD）")).toBeInTheDocument();
    expect(screen.queryByText("人员与 Key")).not.toBeInTheDocument();
  });

  it("identifies one failed dashboard status without retrying or hiding other data", async () => {
    useAuthenticatedHandlers();
    let channelRequests = 0;
    server.use(
      http.get("/api/v1/sub2api/connection", () =>
        HttpResponse.json({
          configured: false,
          userScopeMode: "SelectedUsers",
          revision: 0,
        }),
      ),
      http.get("/api/v1/reports", () =>
        HttpResponse.json({
          items: [],
          total: 0,
          page: 1,
          pageSize: 25,
          pages: 0,
        }),
      ),
      http.get("/api/v1/reports/generations", () =>
        HttpResponse.json({
          items: [],
          total: 0,
          page: 1,
          pageSize: 20,
          pages: 0,
        }),
      ),
      http.get("/api/v1/channels", () => {
        channelRequests += 1;
        return HttpResponse.json(
          { title: "Internal Server Error", status: 500 },
          { status: 500 },
        );
      }),
    );

    renderApp();

    expect(
      await screen.findByRole("heading", { level: 1, name: "工作台" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("部分状态加载失败")).toBeInTheDocument();
    expect(
      screen.getByText("发送渠道读取失败，其他已读取数据仍可使用。"),
    ).toBeInTheDocument();
    expect(screen.queryByText("部分运行状态读取失败")).not.toBeInTheDocument();
    await waitFor(() => expect(channelRequests).toBe(1));
  });

  it("renders synchronized API Keys without people concepts", async () => {
    useAuthenticatedHandlers();
    server.use(
      http.get("/api/v1/sub2api/connection", () =>
        HttpResponse.json({
          configured: true,
          hasAdminApiKey: true,
          revision: 1,
        }),
      ),
      http.get("/api/v1/sub2api/keys", () =>
        HttpResponse.json({
          items: [
            {
              id: "00000000-0000-0000-0000-00000000000a",
              externalId: "101",
              sourceUserId: "42",
              sourceUserEmail: "user@example.com",
              name: "合成 Key",
              status: "active",
              groupId: "7",
              lastUsedAt: "2026-08-26T10:00:00Z",
              lastSeenAt: "2026-08-26T10:00:00Z",
              retiredAt: null,
            },
          ],
          total: 1,
          page: 1,
          pageSize: 50,
          pages: 1,
          diagnostics: {
            retiredKeys: 0,
          },
          lastSynchronizedAt: "2026-08-26T10:00:00Z",
        }),
      ),
    );

    renderApp("/keys");

    expect(
      await screen.findByRole("heading", { level: 1, name: "API Keys" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("合成 Key")).toBeInTheDocument();
    expect(screen.getByText(/用户 user@example.com/)).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "仅看历史 Key" }),
    ).toBeInTheDocument();
  });

  it("renders immutable report snapshots and generation controls", async () => {
    useAuthenticatedHandlers();
    server.use(
      http.get("/api/v1/reports", () =>
        HttpResponse.json({
          items: [
            {
              id: "11111111-1111-1111-1111-111111111111",
              schemaVersion: 1,
              status: "Complete",
              trigger: "ManualDryRun",
              cutoffDate: "2026-08-25",
              timezone: "Asia/Shanghai",
              generatedAt: "2026-08-26T12:00:00Z",
              userCount: 2,
              keyCount: 3,
              failedRangeCount: 0,
              sevenDayActualCost: 1.25,
              thirtyDayActualCost: 3.25,
              windows: [
                {
                  key: "rolling_7_days",
                  label: "最近 7 天",
                  startDate: "2026-08-19",
                  endDateExclusive: "2026-08-26",
                  dayCount: 7,
                  totalActualCost: "8.75",
                },
                {
                  key: "rolling_30_days",
                  label: "最近 30 天",
                  startDate: "2026-07-27",
                  endDateExclusive: "2026-08-26",
                  dayCount: 30,
                  totalActualCost: "42.5",
                },
              ],
            },
          ],
          total: 1,
          page: 1,
          pageSize: 25,
          pages: 1,
        }),
      ),
    );

    renderApp("/reports");

    expect(
      await screen.findByRole("heading", { level: 1, name: "报告记录" }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("统计截止日（选填）")).toHaveAttribute("type", "date");
    expect(
      screen.getByRole("button", { name: "生成报告" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("完整")).toBeInTheDocument();
    expect(
      screen.getByRole("columnheader", { name: "用户数（个） / Key 数（个）" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("columnheader", { name: "窗口费用（USD）" }),
    ).toBeInTheDocument();
    expect(screen.getByText("最近 7 天：8.75")).toBeInTheDocument();
    expect(screen.getByText("最近 30 天：42.50")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "查看" })).toHaveAttribute(
      "href",
      "/reports/11111111-1111-1111-1111-111111111111",
    );
  });

  it("renders notification channels with masked secrets", async () => {
    useAuthenticatedHandlers();
    let createRequest: {
      type: string;
      email: unknown;
      webhookUrl: string;
      signSecret: string;
    } | null = null;
    server.use(
      http.get("/api/v1/channels", () =>
        HttpResponse.json([
          {
            id: "44444444-4444-4444-4444-444444444444",
            type: "Email",
            name: "合成邮件渠道",
            enabled: true,
            email: {
              host: "smtp.example.com",
              port: 587,
              security: "StartTls",
              username: "reports@example.com",
              fromAddress: "reports@example.com",
              fromName: "Sub2API Report",
              toAddresses: ["recipient@example.com"],
              ccAddresses: [],
              hasPassword: true,
              passwordMask: "****abcd",
            },
            webhook: null,
            revision: 2,
            createdAt: "2026-08-26T10:00:00Z",
            updatedAt: "2026-08-26T10:00:00Z",
            lastTestedAt: null,
            lastTestSucceeded: null,
            lastTestCode: null,
          },
        ]),
      ),
      http.get("/api/v1/security/antiforgery", () =>
        HttpResponse.json({ token: "channel-token" }),
      ),
      http.post("/api/v1/channels", async ({ request }) => {
        createRequest = (await request.json()) as typeof createRequest;
        return HttpResponse.json(
          { id: "55555555-5555-5555-5555-555555555555" },
          { status: 201 },
        );
      }),
    );

    renderApp("/channels");

    expect(
      await screen.findByRole("heading", { level: 1, name: "发送渠道" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("合成邮件渠道")).toBeInTheDocument();
    expect(screen.getByText(/密码 \*\*\*\*abcd/)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "新增渠道" }),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "新增渠道" }));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
    expect(screen.getByLabelText("渠道名称")).toBeInTheDocument();
    expect(screen.getByLabelText("SMTP 主机")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "邮件（SMTP）" }),
    ).toHaveAttribute("aria-pressed", "true");
    await userEvent.click(
      screen.getByRole("button", { name: "钉钉群机器人" }),
    );
    expect(screen.getByLabelText("Webhook 地址")).toBeInTheDocument();
    expect(screen.queryByLabelText("SMTP 主机")).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "钉钉群机器人" }),
    ).toHaveAttribute("aria-pressed", "true");
    await userEvent.click(
      screen.getByRole("button", { name: "飞书群机器人" }),
    );
    expect(
      screen.getByRole("button", { name: "飞书群机器人" }),
    ).toHaveAttribute("aria-pressed", "true");
    await userEvent.type(screen.getByLabelText("渠道名称"), "合成飞书渠道");
    await userEvent.type(
      screen.getByLabelText("Webhook 地址"),
      "https://open.feishu.cn/open-apis/bot/v2/hook/synthetic-token",
    );
    await userEvent.type(
      screen.getByLabelText("加签密钥"),
      "synthetic-sign-secret",
    );
    await userEvent.click(screen.getByRole("button", { name: "保存渠道" }));
    await waitFor(() => expect(createRequest).not.toBeNull());
    expect(createRequest).toMatchObject({
      type: "Feishu",
      email: null,
      webhookUrl:
        "https://open.feishu.cn/open-apis/bot/v2/hook/synthetic-token",
      signSecret: "synthetic-sign-secret",
    });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("updates schedules and confirms retries with unknown outcomes", async () => {
    useAuthenticatedHandlers();
    let scheduleRevision = 1;
    let retryRequests = 0;
    server.use(
      http.get("/api/v1/channels", () =>
        HttpResponse.json([
          { id: "33333333-3333-3333-3333-333333333333", enabled: true },
        ]),
      ),
      http.get("/api/v1/schedule", () =>
        HttpResponse.json({
          enabled: true,
          dayOfMonth: 1,
          localTime: "09:00",
          timezone: "Asia/Shanghai",
          windows: defaultScheduleWindows,
          revision: scheduleRevision,
          updatedAt: "2026-08-27T09:00:00Z",
          nextRunAt: "2026-09-01T01:00:00Z",
          synchronized: true,
          synchronizationErrorCode: null,
        }),
      ),
      http.get("/api/v1/schedule/runs", () =>
        HttpResponse.json({
          items: [
            {
              id: "66666666-6666-6666-6666-666666666666",
              trigger: "Scheduled",
              status: "Failed",
              reportId: null,
              periodEnd: "2026-08-26",
              timezone: "Asia/Shanghai",
              scheduleRevision: 1,
              retryOfRunId: null,
              attempt: 1,
              startedAt: "2026-08-27T09:00:00Z",
              collectingAt: "2026-08-27T09:00:01Z",
              renderingAt: null,
              deliveringAt: null,
              completedAt: "2026-08-27T09:00:02Z",
              errorCode: "outcome_unknown",
              errorMessage: "任务在重启后保留了未知发送结果。",
              deliveryCount: 1,
              succeededDeliveryCount: 0,
              failedDeliveryCount: 1,
              hasOutcomeUnknown: true,
              canRetry: true,
            },
          ],
          total: 1,
          page: 1,
          pageSize: 20,
          pages: 1,
        }),
      ),
      http.get("/api/v1/security/antiforgery", () =>
        HttpResponse.json({ token: "schedule-token" }),
      ),
      http.put("/api/v1/schedule", async ({ request }) => {
        const body = (await request.json()) as {
          dayOfMonth: number;
          windows: Array<{ key: string }>;
        };
        expect(body.windows.map((spec) => spec.key)).toEqual([
          "rolling_7_days",
          "rolling_30_days",
          "previous_calendar_week",
          "previous_calendar_month",
        ]);
        scheduleRevision += 1;
        return HttpResponse.json({
          enabled: true,
          dayOfMonth: body.dayOfMonth,
          localTime: "09:00",
          timezone: "Asia/Shanghai",
          windows: body.windows,
          revision: scheduleRevision,
          updatedAt: "2026-08-27T09:05:00Z",
          nextRunAt: "2026-09-02T01:00:00Z",
          synchronized: true,
          synchronizationErrorCode: null,
        });
      }),
      http.post("/api/v1/schedule/runs/:runId/retry", async ({ request }) => {
        const body = (await request.json()) as { confirmOutcomeUnknown: boolean };
        expect(body.confirmOutcomeUnknown).toBe(true);
        retryRequests += 1;
        return HttpResponse.json(
          {
            id: "77777777-7777-7777-7777-777777777777",
            trigger: "Retry",
            status: "Queued",
            retryOfRunId: "66666666-6666-6666-6666-666666666666",
            attempt: 2,
            startedAt: "2026-08-27T09:06:00Z",
          },
          { status: 202 },
        );
      }),
    );

    renderApp("/schedule");

    expect(
      await screen.findByRole("heading", { level: 1, name: "计划任务" }),
    ).toBeInTheDocument();
    expect(await screen.findByLabelText("每月日期")).toHaveValue(1);
    expect(screen.getByRole("switch", { name: "启用自动月报" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "滚动 7 天" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "上一完整自然月" })).toBeChecked();
    expect(
      screen.getByText(/当前统计窗口：滚动 7 天、滚动 30 天、上一完整自然周、上一完整自然月/),
    ).toBeInTheDocument();
    expect(await screen.findByText("任务在重启后保留了未知发送结果。")).toBeInTheDocument();

    const dayInput = screen.getByLabelText("每月日期");
    await userEvent.clear(dayInput);
    await userEvent.type(dayInput, "2");
    await userEvent.click(screen.getByRole("button", { name: "保存计划" }));
    expect(await screen.findByText("revision 2")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "重试" }));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText(/可能产生重复消息/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "确认重试" }));
    await waitFor(() => expect(retryRequests).toBe(1));
  });

  it("renders database, security, and synchronized user settings", async () => {
    useAuthenticatedHandlers();
    let usersSynchronized = false;
    let scopeSaveRequests = 0;
    server.use(
      http.get("/api/v1/system/settings", () =>
        HttpResponse.json({
          timezone: "Asia/Shanghai",
          releaseChannel: "stable",
          logLevel: "Information",
          reportConcurrency: 4,
          reportRetentionMonths: 12,
          backupRetentionCount: 10,
          revision: 1,
          updatedAt: null,
        }),
      ),
      http.get("/api/v1/sub2api/connection", () =>
        HttpResponse.json({
          configured: true,
          baseUrl: "https://sub2api.example.com",
          hasAdminApiKey: true,
          adminApiKeyMask: "****1234",
          userScopeMode: "SelectedUsers",
          codexGroupId: "7",
          revision: 1,
          updatedAt: "2026-08-26T10:00:00Z",
          lastTestedAt: null,
          lastTestSucceeded: null,
          lastTestCode: null,
          lastSynchronizedAt: null,
          lastSynchronizedKeyCount: null,
          lastUsersSynchronizedAt: null,
          lastSynchronizedUserCount: null,
        }),
      ),
      http.get("/api/v1/security/antiforgery", () =>
        HttpResponse.json({ token: "settings-token" }),
      ),
      http.post("/api/v1/sub2api/users/sync", () => {
        usersSynchronized = true;
        return HttpResponse.json({
          added: 1,
          updated: 0,
          retired: 0,
          total: 1,
          synchronizedAt: "2026-08-27T09:20:10Z",
          configurationRevision: 1,
        });
      }),
      http.get("/api/v1/sub2api/users", () =>
        HttpResponse.json({
          scopeMode: "SelectedUsers",
          users: usersSynchronized
            ? [
                {
                  id: "55555555-5555-5555-5555-555555555555",
                  externalId: "1",
                  email: "synthetic.user@example.com",
                  status: "active",
                  isSelected: false,
                  lastSeenAt: "2026-08-27T09:20:10Z",
                },
              ]
            : [],
          connectionRevision: 1,
          lastSynchronizedAt: usersSynchronized ? "2026-08-27T09:20:10Z" : null,
        }),
      ),
      http.put("/api/v1/sub2api/users/scope", () => {
        scopeSaveRequests += 1;
        return HttpResponse.json({
          scopeMode: "SelectedUsers",
          users: [
            {
              id: "55555555-5555-5555-5555-555555555555",
              externalId: "1",
              email: "synthetic.user@example.com",
              status: "active",
              isSelected: true,
              lastSeenAt: "2026-08-27T09:20:10Z",
            },
          ],
          connectionRevision: 2,
          lastSynchronizedAt: "2026-08-27T09:20:10Z",
        });
      }),
    );

    renderApp("/settings");

    expect(
      await screen.findByRole("heading", { level: 1, name: "系统设置" }),
    ).toBeInTheDocument();
    expect(await screen.findByLabelText("默认时区")).toHaveValue(
      "Asia/Shanghai",
    );
    expect(
      screen.getByRole("heading", { level: 2, name: "管理员安全" }),
    ).toBeInTheDocument();
    expect(
      screen.getByLabelText("当前密码", {
        selector: "#change-current-password",
      }),
    ).toBeInTheDocument();

    const groupIdInput = screen.getByLabelText("Codex Group ID（选填）");
    expect(groupIdInput).toHaveAttribute("placeholder", "例如 123");
    await userEvent.clear(groupIdInput);
    await userEvent.type(groupIdInput, "admin");
    await userEvent.click(screen.getByRole("button", { name: "保存连接配置" }));
    expect(
      await screen.findByText("请输入数字分组 ID（例如 123），不要填写组名"),
    ).toBeInTheDocument();
    expect(
      await screen.findByText("保存连接配置需要敏感操作授权"),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "前往授权" }));
    expect(
      screen.getByLabelText("当前密码", { selector: "#step-up-password" }),
    ).toHaveFocus();

    await userEvent.click(screen.getByRole("button", { name: "获取指南" }));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText(/重新生成会使旧 Key 失效/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "知道了" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "同步用户" }));
    expect(await screen.findByText("用户同步完成")).toBeInTheDocument();
    expect(
      await screen.findByText("synthetic.user@example.com"),
    ).toBeInTheDocument();

    const userRow = screen
      .getByText("synthetic.user@example.com")
      .closest("label");
    expect(userRow).not.toBeNull();
    await userEvent.click(
      within(userRow as HTMLElement).getByRole("checkbox"),
    );
    await userEvent.click(
      screen.getByRole("button", { name: "保存统计范围" }),
    );
    await waitFor(() => expect(scopeSaveRequests).toBe(1));
    expect(await screen.findByText("统计范围已保存")).toBeInTheDocument();
    expect(
      screen.getByText("synthetic.user@example.com"),
    ).toBeInTheDocument();
    expect(screen.queryByText("尚未同步用户")).not.toBeInTheDocument();

    const savedRow = screen
      .getByText("synthetic.user@example.com")
      .closest("label");
    expect(savedRow).not.toBeNull();
    await userEvent.click(
      within(savedRow as HTMLElement).getByRole("checkbox"),
    );
    expect(
      screen.queryByText("统计范围已保存"),
    ).not.toBeInTheDocument();

    server.use(
      http.post("/api/v1/auth/step-up", () =>
        HttpResponse.json({
          username: "synthetic-admin",
          sessionStartedAt: "2026-08-27T09:00:00Z",
          stepUpExpiresAt: "2099-01-01T00:00:00Z",
        }),
      ),
    );
    await userEvent.type(
      screen.getByLabelText("当前密码", { selector: "#step-up-password" }),
      "synthetic-password",
    );
    await userEvent.click(screen.getByRole("button", { name: "确认密码" }));
    expect(
      await screen.findByText(/现在可以保存连接配置/),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "前往授权" }),
    ).not.toBeInTheDocument();
  });

  it("falls back to legacy seven/thirty day costs when list windows are missing", async () => {
    useAuthenticatedHandlers();
    server.use(
      http.get("/api/v1/reports", () =>
        HttpResponse.json({
          items: [
            {
              id: "12121212-1212-1212-1212-121212121212",
              schemaVersion: 1,
              status: "Complete",
              trigger: "Scheduled",
              cutoffDate: "2026-08-25",
              timezone: "Asia/Shanghai",
              generatedAt: "2026-08-26T12:00:00Z",
              userCount: 2,
              keyCount: 3,
              failedRangeCount: 0,
              sevenDayActualCost: "1.25",
              thirtyDayActualCost: "3.25",
            },
          ],
          total: 1,
          page: 1,
          pageSize: 25,
          pages: 1,
        }),
      ),
    );

    renderApp("/reports");

    expect(
      await screen.findByText("最近 7 天：1.25"),
    ).toBeInTheDocument();
    expect(screen.getByText("最近 30 天：3.25")).toBeInTheDocument();
  });

  it("submits default windows and renders switchable usage rankings", async () => {
    useAuthenticatedHandlers();
    let dryRunCount = 0;
    let dryRunBody: {
      cutoffDate: string | null;
      windows: Array<{ key: string }> | null;
    } | null = null;
    server.use(
      http.get("/api/v1/reports", () =>
        HttpResponse.json({
          items: [],
          total: 0,
          page: 1,
          pageSize: 25,
          pages: 0,
        }),
      ),
      http.get("/api/v1/reports/generations", () =>
        HttpResponse.json({
          items: [],
          total: 0,
          page: 1,
          pageSize: 20,
          pages: 0,
        }),
      ),
      http.get("/api/v1/security/antiforgery", () =>
        HttpResponse.json({ token: "report-token" }),
      ),
      http.post("/api/v1/reports/dry-run", async ({ request }) => {
        dryRunCount += 1;
        dryRunBody = (await request.json()) as typeof dryRunBody;
        return HttpResponse.json(dynamicReportDetail);
      }),
      http.get("/api/v1/reports/:id", () =>
        HttpResponse.json(dynamicReportDetail),
      ),
    );

    renderApp("/reports");

    expect(
      await screen.findByRole("heading", { level: 1, name: "报告记录" }),
    ).toBeInTheDocument();
    for (const name of [
      "滚动 7 天",
      "滚动 30 天",
      "上一完整自然周",
      "上一完整自然月",
    ]) {
      expect(screen.getByRole("checkbox", { name })).toBeChecked();
    }

    await userEvent.click(screen.getByRole("button", { name: "生成报告" }));

    expect(
      await screen.findByRole("heading", { level: 1, name: "2026年8月26日 报告" }),
    ).toBeInTheDocument();
    expect(dryRunCount).toBe(1);
    expect(dryRunBody?.cutoffDate).toBeNull();
    expect(dryRunBody?.windows?.map((spec) => spec.key)).toEqual([
      "rolling_7_days",
      "rolling_30_days",
      "previous_calendar_week",
      "previous_calendar_month",
    ]);
    const overview = screen.getByRole("region", { name: "用量概览" });
    expect(within(overview).getByText("$1.25")).toBeInTheDocument();
    expect(within(overview).getByText("12")).toBeInTheDocument();
    expect(within(overview).getByText("480")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "最近 7 天" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(
      screen.getByRole("heading", { level: 2, name: "用户费用排行" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Key 费用排行" }),
    ).toBeInTheDocument();
    expect(screen.getAllByLabelText("第 1 名")).toHaveLength(2);
    expect(
      screen.getAllByRole("columnheader", { name: "实际费用（USD）" }),
    ).toHaveLength(2);

    await userEvent.click(
      screen.getByRole("button", { name: "上周" }),
    );
    expect(
      screen.getByRole("button", { name: "上周" }),
    ).toHaveAttribute("aria-pressed", "true");

    expect(within(overview).getByText("$3.50")).toBeInTheDocument();
    expect(within(overview).getByText("30")).toBeInTheDocument();
    expect(within(overview).getByText("1,200")).toBeInTheDocument();
    expect(screen.getAllByLabelText("费用占比 100.0%")).toHaveLength(2);
    expect(
      screen.getByText(/窗口 上周（previous_calendar_week）/),
    ).toBeInTheDocument();
    expect(screen.getByText(/2026-08-17 至 2026-08-23/)).toBeInTheDocument();
    expect(screen.getByText(/· E502/)).toBeInTheDocument();
  });

  it("validates report generation windows and submits a custom range", async () => {
    useAuthenticatedHandlers();
    let dryRunCount = 0;
    let dryRunBody: {
      cutoffDate: string | null;
      windows: Array<{
        key: string;
        kind: string;
        customStartDate: string | null;
        customEndDate: string | null;
      }> | null;
    } | null = null;
    server.use(
      http.get("/api/v1/reports", () =>
        HttpResponse.json({
          items: [],
          total: 0,
          page: 1,
          pageSize: 25,
          pages: 0,
        }),
      ),
      http.get("/api/v1/reports/generations", () =>
        HttpResponse.json({
          items: [],
          total: 0,
          page: 1,
          pageSize: 20,
          pages: 0,
        }),
      ),
      http.get("/api/v1/security/antiforgery", () =>
        HttpResponse.json({ token: "report-token" }),
      ),
      http.post("/api/v1/reports/dry-run", async ({ request }) => {
        dryRunCount += 1;
        dryRunBody = (await request.json()) as typeof dryRunBody;
        return HttpResponse.json(dynamicReportDetail);
      }),
      http.get("/api/v1/reports/:id", () =>
        HttpResponse.json(dynamicReportDetail),
      ),
    );

    renderApp("/reports");

    expect(
      await screen.findByRole("heading", { level: 1, name: "报告记录" }),
    ).toBeInTheDocument();

    for (const name of [
      "滚动 7 天",
      "滚动 30 天",
      "上一完整自然周",
      "上一完整自然月",
    ]) {
      await userEvent.click(screen.getByRole("checkbox", { name }));
    }
    await userEvent.click(screen.getByRole("button", { name: "生成报告" }));
    expect(
      await screen.findByText("至少选择一个统计窗口"),
    ).toBeInTheDocument();
    expect(dryRunCount).toBe(0);

    await userEvent.type(
      screen.getByLabelText("自定义开始日（选填）"),
      "2026-08-01",
    );
    await userEvent.type(
      screen.getByLabelText("自定义结束日（选填）"),
      "2026-08-26",
    );
    await userEvent.type(screen.getByLabelText("统计截止日（选填）"), "2026-08-25");
    await userEvent.click(screen.getByRole("button", { name: "生成报告" }));
    expect(
      await screen.findByText("自定义区间结束日不能晚于统计截止日"),
    ).toBeInTheDocument();
    expect(dryRunCount).toBe(0);

    await userEvent.clear(screen.getByLabelText("自定义结束日（选填）"));
    await userEvent.type(
      screen.getByLabelText("自定义结束日（选填）"),
      "2026-08-25",
    );
    await userEvent.click(screen.getByRole("button", { name: "生成报告" }));

    expect(dryRunCount).toBe(1);
    expect(dryRunBody?.cutoffDate).toBe("2026-08-25");
    expect(dryRunBody?.windows).toEqual([
      {
        key: "custom_range",
        kind: "CustomRange",
        rollingDays: null,
        weekStartsOn: null,
        customStartDate: "2026-08-01",
        customEndDate: "2026-08-25",
      },
    ]);
  });

  it("refreshes an expired antiforgery token and retries once", async () => {
    let tokenRequests = 0;
    let updateRequests = 0;
    let firstToken: string | null = null;
    server.use(
      http.get("/api/v1/security/antiforgery", () => {
        tokenRequests += 1;
        return HttpResponse.json({ token: `token-${tokenRequests}` });
      }),
      http.put("/api/v1/system/settings", ({ request }) => {
        updateRequests += 1;
        if (updateRequests === 1) {
          firstToken = request.headers.get("X-CSRF-TOKEN");
          return HttpResponse.json(
            {
              title: "Bad Request",
              status: 400,
              detail: "Antiforgery token 无效或已过期。",
            },
            { status: 400 },
          );
        }

        const retriedToken = request.headers.get("X-CSRF-TOKEN");
        expect(retriedToken).toBeTruthy();
        expect(retriedToken).not.toBe(firstToken);
        return HttpResponse.json({
          timezone: "Asia/Shanghai",
          releaseChannel: "stable",
          logLevel: "Information",
          reportConcurrency: 4,
          reportRetentionMonths: 12,
          backupRetentionCount: 10,
          revision: 2,
          updatedAt: "2026-08-27T09:00:00Z",
        });
      }),
    );

    await expect(
      updateSystemSettings({
        timezone: "Asia/Shanghai",
        releaseChannel: "stable",
        logLevel: "Information",
        reportConcurrency: 4,
        reportRetentionMonths: 12,
        backupRetentionCount: 10,
        revision: 1,
      }),
    ).resolves.toMatchObject({ revision: 2 });
    expect(tokenRequests).toBeGreaterThanOrEqual(1);
    expect(updateRequests).toBe(2);
  });
});
