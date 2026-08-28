import { describe, expect, it } from "vitest";
import {
  formatCost,
  formatUsd,
  formatCount,
  toInclusiveEndDate,
} from "@/features/reports/report-format";

describe("report formatters", () => {
  it("formats report costs as plain numbers and preserves large integer counts", () => {
    expect(formatCost("3.25")).toBe("3.25");
    expect(formatCost("7083.582731")).toBe("7,083.582731");
    expect(formatUsd("7083.582731")).toBe("$7,083.58");
    expect(formatCount("9007199254740993")).toBe("9,007,199,254,740,993");
  });

  it("converts half-open exclusive end dates into user-visible closed end dates", () => {
    expect(toInclusiveEndDate("2026-08-27")).toBe("2026-08-26");
    expect(toInclusiveEndDate("2026-09-01")).toBe("2026-08-31");
    expect(toInclusiveEndDate("2026-01-01")).toBe("2025-12-31");
  });
});
