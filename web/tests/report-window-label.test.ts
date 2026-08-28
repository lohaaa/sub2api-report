import { describe, expect, it } from "vitest";
import {
  getReportWindowDisplayLabel,
  normalizeReportWindowLabel,
} from "@/features/reports/report-window-label";

const baseWindow = {
  key: "previous_calendar_week",
  kind: "PreviousCalendarWeek" as const,
  rollingDays: null,
  weekStartsOn: "Monday" as const,
  startDate: "2026-08-17",
  endDateExclusive: "2026-08-24",
  dayCount: 7,
  label: "上一完整自然周",
};

describe("report window labels", () => {
  it("uses concise labels for new and historical calendar windows", () => {
    expect(getReportWindowDisplayLabel(baseWindow)).toBe("上一自然周");
    expect(
      getReportWindowDisplayLabel({
        ...baseWindow,
        key: "previous_calendar_month",
        kind: "PreviousCalendarMonth",
        weekStartsOn: null,
        label: "上一完整自然月",
      }),
    ).toBe("上一自然月");
    expect(normalizeReportWindowLabel("上一完整自然周")).toBe("上一自然周");
  });
});
