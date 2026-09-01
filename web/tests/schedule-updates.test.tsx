import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ThemeProvider } from "@/app/theme-provider";
import App from "@/App";
import { server } from "./setup";

function renderApp(route: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(renderAppTree(route, queryClient));
  return queryClient;
}

function renderAppTree(route: string, queryClient: QueryClient) {
  return (
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <TooltipProvider>
          <MemoryRouter initialEntries={[route]}>
            <App />
          </MemoryRouter>
        </TooltipProvider>
      </QueryClientProvider>
    </ThemeProvider>
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
      }),
    ),
    http.get("/api/v1/schedule", () =>
      HttpResponse.json({
        enabled: false,
        dayOfMonth: 1,
        shortMonthStrategy: "UseLastDay",
        localTime: "09:00",
        timezone: "Asia/Shanghai",
        windows: [
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
        ],
        revision: 1,
        updatedAt: null,
        nextRunAt: null,
        synchronized: true,
        synchronizationErrorCode: null,
      }),
    ),
    http.get("/api/v1/schedule/runs", () =>
      HttpResponse.json({
        items: [],
        total: 0,
        page: 1,
        pageSize: 20,
        pages: 0,
      }),
    ),
    http.get("/api/v1/channels", () => HttpResponse.json([])),
    http.get("/api/v1/security/antiforgery", () =>
      HttpResponse.json({ token: "synthetic-csrf-token" }),
    ),
  );
}

async function selectScheduleDay(dayLabel: string) {
  const user = userEvent.setup();
  await user.click(await screen.findByRole("combobox", { name: "每月日期" }));
  const option = await screen.findByRole("option", { name: dayLabel });
  await user.click(option);
}

describe("schedule page short month strategy", () => {
  beforeEach(() => {
    useAuthenticatedHandlers();
  });

  it("selects the month day without any numeric day input", async () => {
    renderApp("/schedule");

    const daySelect = await screen.findByRole("combobox", { name: "每月日期" });
    expect(daySelect).toHaveAttribute("role", "combobox");
    expect(screen.queryByRole("spinbutton", { name: "每月日期" })).not.toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(daySelect);
    const options = await screen.findAllByRole("option");
    expect(options).toHaveLength(31);
    await user.click(await screen.findByRole("option", { name: "每月 12 日" }));
    expect(await screen.findByRole("combobox", { name: "每月日期" })).toHaveTextContent(
      "12",
    );
  });

  it("hides the strategy choice on days within short months but keeps it on day 31", async () => {
    renderApp("/schedule");

    expect(await screen.findByRole("combobox", { name: "每月日期" })).toBeInTheDocument();
    expect(screen.queryByRole("group", { name: "短月执行策略" })).not.toBeInTheDocument();

    await selectScheduleDay("每月 31 日");

    expect(
      await screen.findByRole("group", { name: "短月执行策略" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "短月取月末", pressed: true }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "跳过该月", pressed: false }),
    ).toBeInTheDocument();
  });

  it("saves a chosen skip strategy and shows the saved confirmation", async () => {
    let savedPayload: Record<string, unknown> | null = null;
    server.use(
      http.put("/api/v1/schedule", async ({ request }) => {
        savedPayload = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          enabled: false,
          dayOfMonth: 31,
          shortMonthStrategy: "SkipMonth",
          localTime: "09:00",
          timezone: "Asia/Shanghai",
          windows: defaultWindows(),
          revision: 2,
          updatedAt: "2026-09-01T02:00:00Z",
          nextRunAt: "2026-10-31T09:00:00+08:00",
          synchronized: true,
          synchronizationErrorCode: null,
        });
      }),
    );
    renderApp("/schedule");

    await selectScheduleDay("每月 31 日");
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: "跳过该月" }));
    await user.click(
      await screen.findByRole("checkbox", { name: "滚动 7 天" }),
    );
    await user.click(screen.getByRole("button", { name: "保存计划" }));

    expect(await screen.findByText("已保存")).toBeInTheDocument();
    expect(savedPayload).not.toBeNull();
    expect(savedPayload?.dayOfMonth).toBe(31);
    expect(savedPayload?.shortMonthStrategy).toBe("SkipMonth");
  });

  it("renders friendly synchronization failures without exposing raw codes", async () => {
    server.use(
      http.get("/api/v1/schedule", () =>
        HttpResponse.json({
          enabled: true,
          dayOfMonth: 31,
          shortMonthStrategy: "UseLastDay",
          localTime: "09:00",
          timezone: "Asia/Shanghai",
          windows: defaultWindows(),
          revision: 3,
          updatedAt: null,
          nextRunAt: null,
          synchronized: false,
          synchronizationErrorCode: "trigger_mismatch",
        }),
      ),
    );
    renderApp("/schedule");

    expect(
      await screen.findByText("持久化计划触发器定义与配置不一致，请重新保存计划。"),
    ).toBeInTheDocument();
    expect(screen.queryByText(/trigger_mismatch/)).not.toBeInTheDocument();
  });
});

function defaultWindows() {
  return [
    {
      key: "rolling_7_days",
      kind: "RollingDays",
      rollingDays: 7,
      weekStartsOn: null,
      customStartDate: null,
      customEndDate: null,
    },
  ];
}
