import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import {
  FileSpreadsheetIcon,
  MailIcon,
  MessageSquareTextIcon,
  PaperclipIcon,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Separator } from "@/components/ui/separator";
import { channelPresentations } from "@/features/channels/channel-presentation";
import {
  getReportWindowMetrics,
  getSystemSettings,
  type NotificationChannelType,
  type ReportDetail,
  type ReportUsageMetrics,
  type ReportWindowDescriptor,
} from "@/lib/api-client";
import {
  formatCost,
  formatCount,
  toInclusiveEndDate,
} from "./report-format";
import { getReportWindowDisplayLabel } from "./report-window-label";


export function ReportChannelPreviewDialog({
  open,
  channelType,
  report,
  onOpenChange,
}: {
  open: boolean;
  channelType: NotificationChannelType;
  report: ReportDetail;
  onOpenChange: (open: boolean) => void;
}) {
  const settingsQuery = useQuery({
    queryKey: ["system-settings"],
    queryFn: ({ signal }) => getSystemSettings(signal),
    enabled: open,
  });
  const downloadPolicy = settingsQuery.data?.reportExternalBaseUrl
    ? formatDownloadPolicy(
        settingsQuery.data.reportDownloadLinkHours,
        settingsQuery.data.reportDownloadMaxDownloads,
      )
    : null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>
            {channelPresentations[channelType].shortLabel}报告消息预览
          </DialogTitle>
          <DialogDescription>
            基于当前报告展示发送结构；收件人和下载令牌使用合成占位值。
          </DialogDescription>
        </DialogHeader>

        {channelType === "Email" ? (
          <EmailPreview report={report} />
        ) : channelType === "DingTalk" ? (
          <DingTalkPreview report={report} downloadPolicy={downloadPolicy} />
        ) : (
          <FeishuPreview report={report} downloadPolicy={downloadPolicy} />
        )}
      </DialogContent>
    </Dialog>
  );
}

function EmailPreview({ report }: { report: ReportDetail }) {
  const dateRange = getReportDateRange(report);
  return (
    <div className="overflow-hidden rounded-lg border" aria-label="邮件消息示例">
      <div className="grid gap-2 bg-muted/50 px-4 py-3 text-xs sm:grid-cols-[5rem_1fr]">
        <span className="text-muted-foreground">主题</span>
        <strong>[Codex 用量报告] {dateRange}</strong>
        <span className="text-muted-foreground">收件人</span>
        <span>recipient@example.com</span>
      </div>
      <div className="bg-foreground px-5 py-5 text-background">
        <span className="text-xs font-semibold text-background/70">
          SUB2API REPORT
        </span>
        <h3 className="mt-1 text-lg font-semibold">Codex 用量报告</h3>
        <p className="mt-1 text-xs text-background/70">
          {dateRange} · {report.timezone} · {statusLabel(report)}
        </p>
      </div>
      <div className="flex flex-col gap-5 p-5">
        <PreviewOverview report={report} />
        <WindowSections report={report} variant="email" />
        <Separator />
        <div className="flex items-center gap-3 text-sm">
          <PaperclipIcon aria-hidden="true" className="size-4 text-muted-foreground" />
          <div className="flex min-w-0 flex-col">
            <strong className="truncate">{getAttachmentName(report)}</strong>
            <span className="text-xs text-muted-foreground">
              XLSX 工作簿 · 完整 Key 明细
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}

function DingTalkPreview({
  report,
  downloadPolicy,
}: {
  report: ReportDetail;
  downloadPolicy: string | null;
}) {
  return (
    <MessagePreviewFrame
      icon={MessageSquareTextIcon}
      label="钉钉群机器人 · Markdown"
      badges={["Markdown 摘要", "无直接附件"]}
    >
      <h3 className="text-base font-semibold">Codex 用量摘要</h3>
      <p className="border-l-2 pl-3 text-xs text-muted-foreground">
        {statusLabel(report)} · 统计时区 {report.timezone} · {report.windows.length} 个窗口 ·{" "}
        {report.users.length} 个用户 · {report.keys.length} 个 Key
      </p>
      <WindowSections report={report} variant="dingtalk" />
      <UserExamples report={report} variant="dingtalk" />
      <DownloadLinkExample policy={downloadPolicy} />
    </MessagePreviewFrame>
  );
}

function FeishuPreview({
  report,
  downloadPolicy,
}: {
  report: ReportDetail;
  downloadPolicy: string | null;
}) {
  return (
    <MessagePreviewFrame
      icon={MessageSquareTextIcon}
      label="飞书群机器人 · post 富文本"
      badges={["富文本摘要", "无直接附件"]}
    >
      <h3 className="text-base font-semibold">Codex 用量摘要</h3>
      <p className="text-xs text-muted-foreground">
        {statusLabel(report)}｜统计时区 {report.timezone}｜{report.windows.length} 个窗口｜
        {report.users.length} 个用户｜{report.keys.length} 个 Key
      </p>
      <WindowSections report={report} variant="feishu" />
      <UserExamples report={report} variant="feishu" />
      <DownloadLinkExample policy={downloadPolicy} />
    </MessagePreviewFrame>
  );
}

function MessagePreviewFrame({
  icon: Icon,
  label,
  badges,
  children,
}: {
  icon: typeof MailIcon;
  label: string;
  badges: string[];
  children: React.ReactNode;
}) {
  return (
    <div className="overflow-hidden rounded-lg border" aria-label={`${label}消息示例`}>
      <div className="flex flex-wrap items-center justify-between gap-2 border-b bg-muted/50 px-4 py-3">
        <div className="flex items-center gap-2 text-sm font-medium">
          <Icon aria-hidden="true" className="size-4" />
          {label}
        </div>
        <div className="flex flex-wrap gap-1.5">
          {badges.map((badge) => (
            <Badge key={badge} variant="outline">
              {badge}
            </Badge>
          ))}
        </div>
      </div>
      <div className="flex flex-col gap-4 p-5">{children}</div>
    </div>
  );
}

function PreviewOverview({ report }: { report: ReportDetail }) {
  return (
    <dl className="grid grid-cols-3 gap-4 text-sm">
      <div>
        <dt className="text-xs text-muted-foreground">统计窗口</dt>
        <dd className="mt-1 font-semibold tabular-nums">{report.windows.length}</dd>
      </div>
      <div>
        <dt className="text-xs text-muted-foreground">Sub2API 用户</dt>
        <dd className="mt-1 font-semibold tabular-nums">{report.users.length}</dd>
      </div>
      <div>
        <dt className="text-xs text-muted-foreground">API Key</dt>
        <dd className="mt-1 font-semibold tabular-nums">{report.keys.length}</dd>
      </div>
    </dl>
  );
}

function WindowSections({
  report,
  variant,
}: {
  report: ReportDetail;
  variant: "email" | "dingtalk" | "feishu";
}) {
  return (
    <div className="flex flex-col gap-4">
      {report.windows.map((window) => {
        const metrics = getReportWindowMetrics(report.windowTotals, window.key);
        const label = getReportWindowDisplayLabel(window);
        return (
          <div key={window.key} className="flex flex-col gap-2">
            {variant === "feishu" ? (
              <strong className="text-sm">【{label}】{formatWindowRange(window)}</strong>
            ) : (
              <div>
                <h4 className="text-sm font-semibold">{label}</h4>
                <p className="text-xs text-muted-foreground">{formatWindowRange(window)}</p>
              </div>
            )}
            <MetricSummary metrics={metrics} variant={variant} />
          </div>
        );
      })}
    </div>
  );
}

function MetricSummary({
  metrics,
  variant,
}: {
  metrics: ReportUsageMetrics | null;
  variant: "email" | "dingtalk" | "feishu";
}) {
  const requests = metrics ? formatCount(metrics.totalRequests) : "0";
  const tokens = metrics ? formatCount(metrics.totalTokens) : "0";
  const cost = metrics ? formatCost(metrics.totalActualCost) : "0";
  if (variant === "email") {
    return (
      <dl className="grid grid-cols-3 gap-3 border bg-muted/30 px-3 py-2.5 text-xs">
        <div>
          <dt className="text-muted-foreground">请求数（次）</dt>
          <dd className="mt-1 font-semibold tabular-nums">{requests}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">Token 数（个）</dt>
          <dd className="mt-1 font-semibold tabular-nums">{tokens}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">实际费用（USD）</dt>
          <dd className="mt-1 font-semibold tabular-nums">{cost}</dd>
        </div>
      </dl>
    );
  }
  if (variant === "dingtalk") {
    return (
      <ul className="flex flex-col gap-1 text-xs">
        <li>• 请求数（次）：<strong>{requests}</strong></li>
        <li>• Token 数（个）：<strong>{tokens}</strong></li>
        <li>• 实际费用（USD）：<strong>{cost}</strong></li>
      </ul>
    );
  }
  return (
    <p className="text-xs">
      合计｜请求数（次） {requests}｜Token 数（个） {tokens}｜实际费用（USD） {cost}
    </p>
  );
}

function UserExamples({
  report,
  variant,
}: {
  report: ReportDetail;
  variant: "dingtalk" | "feishu";
}) {
  const users = report.users.slice(0, 2);
  if (users.length === 0) return null;
  return (
    <div className="flex flex-col gap-2 border-t pt-3 text-xs">
      <strong>Sub2API 用户明细</strong>
      {users.map((user, index) => (
        <div key={user.userId}>
          {variant === "dingtalk" ? "• " : `${index + 1}. `}
          <strong>{user.email}</strong>（Key 数（个） {user.keyCount}）
        </div>
      ))}
      {report.users.length > users.length ? (
        <span className="text-muted-foreground">其余用户按相同结构继续展示</span>
      ) : null}
    </div>
  );
}

function DownloadLinkExample({ policy }: { policy: string | null }) {
  if (!policy) {
    return (
      <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3 text-xs">
        <p className="text-muted-foreground">
          未配置外部访问地址，本次消息不含下载链接。请前往“系统设置 →
          动态配置 → 群报告下载授权”完成配置。
        </p>
        <Link
          to="/settings#report-download-settings"
          className="font-medium text-primary underline underline-offset-4"
        >
          前往配置
        </Link>
      </div>
    );
  }
  return (
    <div className="flex items-center gap-2 border-t pt-3 text-xs">
      <FileSpreadsheetIcon aria-hidden="true" className="size-4 text-muted-foreground" />
      <span className="font-medium text-primary underline underline-offset-4">
        下载 XLSX 完整明细（{policy}）
      </span>
    </div>
  );
}

function formatWindowRange(window: ReportWindowDescriptor) {
  return `${window.startDate} 至 ${toInclusiveEndDate(window.endDateExclusive)}，共 ${window.dayCount} 天`;
}

function getReportDateRange(report: ReportDetail) {
  const starts = report.windows.map((window) => window.startDate).sort();
  const ends = report.windows
    .map((window) => toInclusiveEndDate(window.endDateExclusive))
    .sort();
  const fallback = report.generatedAt.slice(0, 10);
  return `${starts[0] ?? fallback} 至 ${ends.at(-1) ?? fallback}`;
}

function getAttachmentName(report: ReportDetail) {
  const endDates = report.windows
    .map((window) => toInclusiveEndDate(window.endDateExclusive))
    .sort();
  return `sub2api-report-${endDates.at(-1) ?? report.generatedAt.slice(0, 10)}.xlsx`;
}

function statusLabel(report: ReportDetail) {
  return report.status === "Partial" ? "部分完成" : "完整报告";
}

function formatDownloadPolicy(hours: number, maxDownloads: number | null) {
  const lifetime = hours % 24 === 0 ? `${hours / 24} 天` : `${hours} 小时`;
  return maxDownloads === null
    ? `${lifetime}内有效，下载次数不限`
    : `${lifetime}内有效，最多下载 ${maxDownloads} 次`;
}
