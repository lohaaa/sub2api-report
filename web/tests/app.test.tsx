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
        version: "0.6.0",
        environment: "Test",
        releaseChannel: "stable",
      }),
    ),
  );
}

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
    expect(await screen.findByText("v0.6.0")).toBeInTheDocument();
    expect(await screen.findByText("已配置")).toBeInTheDocument();
    expect(screen.getByText("1 个用户")).toBeInTheDocument();
    expect(screen.getByText("17 个 Key")).toBeInTheDocument();
    expect(screen.queryByText("人员与 Key")).not.toBeInTheDocument();
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
              personCount: 2,
              keyCount: 3,
              failedSegmentCount: 0,
              unassignedSegmentCount: 0,
              sevenDayActualCost: 1.25,
              thirtyDayActualCost: 3.25,
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
    expect(screen.getByLabelText("统计截止日")).toHaveAttribute("type", "date");
    expect(
      screen.getByRole("button", { name: "生成报告" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("完整")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "查看" })).toHaveAttribute(
      "href",
      "/reports/11111111-1111-1111-1111-111111111111",
    );
  });

  it("renders notification channels with masked secrets", async () => {
    useAuthenticatedHandlers();
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
    await userEvent.click(screen.getByRole("button", { name: "取消" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
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
