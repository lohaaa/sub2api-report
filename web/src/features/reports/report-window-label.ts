import type { ReportWindowDescriptor } from "@/lib/api-client";

export function getReportWindowDisplayLabel(window: ReportWindowDescriptor) {
  if (window.kind === "PreviousCalendarWeek") return "上一自然周";
  if (window.kind === "PreviousCalendarMonth") return "上一自然月";
  return normalizeReportWindowLabel(window.label);
}

export function normalizeReportWindowLabel(label: string) {
  if (label === "上一完整自然周") return "上一自然周";
  if (label === "上一完整自然月") return "上一自然月";
  return label;
}
