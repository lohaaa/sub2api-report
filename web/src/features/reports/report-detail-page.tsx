import { useQuery } from "@tanstack/react-query";
import {
  AlertTriangleIcon,
  ArrowLeftIcon,
  DownloadIcon,
} from "lucide-react";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { PageHeader } from "@/components/layout/page-header";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button, buttonVariants } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { Skeleton } from "@/components/ui/skeleton";
import { Spinner } from "@/components/ui/spinner";
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { FormError } from "@/features/auth/form-error";
import { ReportDeliveryPanel } from "@/features/reports/report-delivery-panel";
import {
  ApiError,
  downloadReportCsv,
  getReport,
  getReportWindowMetrics,
  type ReportKeyUsage,
  type ReportUserUsage,
  type ReportUsageMetrics,
  type ReportWindowDescriptor,
} from "@/lib/api-client";
import {
  formatCost,
  formatUsd,
  formatCount,
  formatDate,
  formatTimestamp,
  toInclusiveEndDate,
} from "./report-format";
import { cn } from "@/lib/utils";
import { ReportStatusBadge } from "./reports-page";

export function ReportDetailPage() {
  const { id = "" } = useParams();
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [isDownloading, setIsDownloading] = useState(false);
  const [selectedWindowKey, setSelectedWindowKey] = useState("");
  const reportQuery = useQuery({
    queryKey: ["report", id],
    queryFn: ({ signal }) => getReport(id, signal),
    enabled: Boolean(id),
    retry: (failureCount, error) =>
      !(error instanceof ApiError && error.status === 404) && failureCount < 1,
  });

  async function handleDownload() {
    setDownloadError(null);
    setIsDownloading(true);
    try {
      const file = await downloadReportCsv(id);
      const url = URL.createObjectURL(file.blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = file.fileName;
      anchor.click();
      window.setTimeout(() => URL.revokeObjectURL(url), 0);
    } catch (error) {
      setDownloadError(
        error instanceof ApiError ? error.message : "CSV 下载失败。",
      );
    } finally {
      setIsDownloading(false);
    }
  }

  if (reportQuery.isPending) {
    return <ReportDetailLoading />;
  }
  if (reportQuery.isError) {
    const notFound =
      reportQuery.error instanceof ApiError && reportQuery.error.status === 404;
    return (
      <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
        <Link
          className={cn(buttonVariants({ variant: "ghost" }), "w-fit")}
          to="/reports"
        >
          <ArrowLeftIcon data-icon="inline-start" />
          返回报告列表
        </Link>
        <FormError
          message={notFound ? "报告不存在或已被清理。" : "无法读取报告详情。"}
        />
      </div>
    );
  }

  const report = reportQuery.data;
  const diagnostics = report.diagnostics;
  const windows = report.windows;
  const totalsByWindowKey = new Map(
    report.windowTotals.map((item) => [item.windowKey, item.metrics]),
  );
  const windowLabelByKey = new Map(
    windows.map((window) => [window.key, getWindowDisplayLabel(window)]),
  );
  const activeWindow =
    windows.find((window) => window.key === selectedWindowKey) ?? windows[0] ?? null;
  const activeWindowKey = activeWindow?.key ?? "";
  const activeMetrics = activeWindow
    ? (totalsByWindowKey.get(activeWindow.key) ?? null)
    : null;
  const rankedUsers = activeWindow
    ? rankUsage(report.users, (user) =>
        getReportWindowMetrics(user.windows, activeWindow.key),
      )
    : [];
  const rankedKeys = activeWindow
    ? rankUsage(report.keys, (key) =>
        getReportWindowMetrics(key.windows, activeWindow.key),
      )
    : [];
  const lastExclusiveEndDate = windows.reduce<string | null>(
    (acc, window) =>
      acc === null || window.endDateExclusive > acc ? window.endDateExclusive : acc,
    null,
  );
  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-2">
          <Link
            className={cn(buttonVariants({ variant: "ghost" }), "w-fit px-0")}
            to="/reports"
          >
            <ArrowLeftIcon data-icon="inline-start" />
            返回报告列表
          </Link>
          <div className="flex flex-wrap items-center gap-2">
            <PageHeader
              title={
                lastExclusiveEndDate
                  ? `${formatDate(toInclusiveEndDate(lastExclusiveEndDate))} 报告`
                  : "用量报告"
              }
              description={`共 ${windows.length} 个统计窗口 · ${report.timezone}`}
            />
            <ReportStatusBadge status={report.status} />
          </div>
        </div>
        <Button
          variant="outline"
          disabled={isDownloading}
          onClick={handleDownload}
        >
          {isDownloading ? (
            <Spinner data-icon="inline-start" />
          ) : (
            <DownloadIcon data-icon="inline-start" />
          )}
          下载 CSV
        </Button>
      </div>

      <FormError message={downloadError} />
      {report.status === "Partial" ? (
        <Alert variant="destructive">
          <AlertTriangleIcon aria-hidden="true" />
          <AlertTitle>报告数据不完整</AlertTitle>
          <AlertDescription>
            失败区间数（个）：{diagnostics.failedRanges.length}。
          </AlertDescription>
        </Alert>
      ) : null}
      {diagnostics.failedRanges.length > 0 ? (
        <Alert variant="destructive">
          <AlertTriangleIcon aria-hidden="true" />
          <AlertTitle>采集失败区间</AlertTitle>
          <AlertDescription>
            <ul className="mt-2 flex flex-col gap-1">
              {diagnostics.failedRanges.map((failure) => {
                const windowLabel =
                  windowLabelByKey.get(failure.windowKey) ?? failure.windowKey;
                return (
                  <li
                    key={`${failure.externalKeyId}-${failure.windowKey}-${failure.startDate}-${failure.endDateExclusive}`}
                    className="text-xs"
                  >
                    {failure.keyName}（ID {failure.externalKeyId}）· 窗口 {windowLabel}
                    （{failure.windowKey}）：{failure.startDate} 至{" "}
                    {toInclusiveEndDate(failure.endDateExclusive)} ·{" "}
                    {failure.errorCode ?? failure.failureKind}
                  </li>
                );
              })}
            </ul>
          </AlertDescription>
        </Alert>
      ) : null}

      <UsageOverview
        window={activeWindow}
        metrics={activeMetrics}
        windows={windows}
        selectedWindowKey={activeWindowKey}
        onWindowChange={setSelectedWindowKey}
        userCount={report.users.length}
        keyCount={report.keys.length}
      />

      <ReportDeliveryPanel
        reportId={report.reportId}
        reportStatus={report.status}
      />

      <section className="flex flex-col gap-3" aria-labelledby="user-ranking-title">
        <RankingHeader
          id="user-ranking-title"
          title="用户费用排行"
          count={`${rankedUsers.length} 个用户`}
          windowLabel={activeWindow ? getWindowDisplayLabel(activeWindow) : undefined}
        />
        <div className="overflow-hidden rounded-lg border">
          <Table className="table-fixed md:table-auto">
            <TableCaption className="sr-only">
              {`${activeWindow ? getWindowDisplayLabel(activeWindow) : "当前窗口"}内按实际费用降序排列的用户用量`}
            </TableCaption>
            <TableHeader>
              <TableRow>
                <TableHead scope="col" className="w-14 text-center">
                  排名
                </TableHead>
                <TableHead scope="col">Sub2API 用户</TableHead>
                <TableHead scope="col" className="hidden text-right sm:table-cell">
                  Key
                </TableHead>
                <TableHead scope="col" className="hidden text-right md:table-cell">
                  请求数
                </TableHead>
                <TableHead scope="col" className="hidden text-right md:table-cell">
                  Token
                </TableHead>
                <TableHead scope="col" className="w-36 text-right">
                  实际费用（USD）
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rankedUsers.length > 0 ? (
                rankedUsers.map((entry, index) => (
                  <UserRankingRow
                    key={entry.item.userId}
                    rank={index + 1}
                    user={entry.item}
                    metrics={entry.metrics}
                    totalCost={activeMetrics?.totalActualCost ?? "0"}
                  />
                ))
              ) : (
                <TableRow>
                  <TableCell
                    colSpan={6}
                    className="h-20 text-center text-muted-foreground"
                  >
                    当前快照没有用户用量
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      </section>

      <section className="flex flex-col gap-3" aria-labelledby="key-ranking-title">
        <RankingHeader
          id="key-ranking-title"
          title="Key 费用排行"
          count={`${rankedKeys.length} 个 Key`}
          windowLabel={activeWindow ? getWindowDisplayLabel(activeWindow) : undefined}
        />
        <div className="overflow-hidden rounded-lg border">
          <Table className="table-fixed md:table-auto">
            <TableCaption className="sr-only">
              {`${activeWindow ? getWindowDisplayLabel(activeWindow) : "当前窗口"}内按实际费用降序排列的 Key 用量`}
            </TableCaption>
            <TableHeader>
              <TableRow>
                <TableHead scope="col" className="w-14 text-center">
                  排名
                </TableHead>
                <TableHead scope="col">Key</TableHead>
                <TableHead scope="col" className="hidden text-right md:table-cell">
                  请求数
                </TableHead>
                <TableHead scope="col" className="hidden text-right md:table-cell">
                  Token
                </TableHead>
                <TableHead scope="col" className="w-36 text-right">
                  实际费用（USD）
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rankedKeys.length > 0 ? (
                rankedKeys.map((entry, index) => (
                  <KeyRankingRow
                    key={entry.item.keyId}
                    rank={index + 1}
                    item={entry.item}
                    metrics={entry.metrics}
                    totalCost={activeMetrics?.totalActualCost ?? "0"}
                  />
                ))
              ) : (
                <TableRow>
                  <TableCell
                    colSpan={5}
                    className="h-16 text-center text-muted-foreground"
                  >
                    当前快照没有 Key 用量
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      </section>

      <p className="text-xs text-muted-foreground">
        快照 v{report.schemaVersion} · 连接配置 revision{" "}
        {report.connectionRevision} · {formatTimestamp(report.generatedAt)}
      </p>
    </div>
  );
}

function UsageOverview({
  window,
  metrics,
  windows,
  selectedWindowKey,
  onWindowChange,
  userCount,
  keyCount,
}: {
  window: ReportWindowDescriptor | null;
  metrics: ReportUsageMetrics | null;
  windows: ReportWindowDescriptor[];
  selectedWindowKey: string;
  onWindowChange: (value: string) => void;
  userCount: number;
  keyCount: number;
}) {
  return (
    <section
      className="flex flex-col gap-5 border-y py-5"
      aria-labelledby="usage-overview-title"
    >
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-1">
          <h2 id="usage-overview-title" className="text-base font-semibold">
            用量概览
          </h2>
          <p className="text-sm text-muted-foreground">
            {window
              ? `${formatDate(window.startDate)} 至 ${formatDate(toInclusiveEndDate(window.endDateExclusive))} · ${window.dayCount} 天`
              : "没有可用的统计窗口"}
          </p>
        </div>
        {windows.length > 0 ? (
          <ToggleGroup
            aria-label="统计窗口"
            value={[selectedWindowKey]}
            onValueChange={(value) => {
              if (value[0]) onWindowChange(value[0]);
            }}
            variant="outline"
            className="grid w-full grid-cols-2 sm:w-fit sm:grid-cols-4"
          >
            {windows.map((item) => (
              <ToggleGroupItem
                key={item.key}
                value={item.key}
                className="w-full min-w-0 px-3"
                title={getWindowDisplayLabel(item)}
              >
                <span className="truncate">{getWindowDisplayLabel(item)}</span>
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
        ) : null}
      </div>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-5 lg:grid-cols-5">
        <SummaryMetric
          label="实际费用（USD）"
          value={metrics ? formatUsd(metrics.totalActualCost) : "—"}
          detail={metrics ? `$${formatCost(metrics.totalActualCost)}` : undefined}
          emphasized
        />
        <SummaryMetric
          label="请求数（次）"
          value={metrics ? formatCount(metrics.totalRequests) : "—"}
        />
        <SummaryMetric
          label="Token 数（个）"
          value={metrics ? formatCount(metrics.totalTokens) : "—"}
        />
        <SummaryMetric label="用户数（个）" value={formatCount(String(userCount))} />
        <SummaryMetric label="Key 数（个）" value={formatCount(String(keyCount))} />
      </dl>
    </section>
  );
}

function SummaryMetric({
  label,
  value,
  detail,
  emphasized = false,
}: {
  label: string;
  value: string;
  detail?: string;
  emphasized?: boolean;
}) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          "truncate font-semibold tabular-nums",
          emphasized ? "text-2xl" : "text-lg",
        )}
        title={detail ?? value}
      >
        {value}
      </dd>
    </div>
  );
}

function RankingHeader({
  id,
  title,
  count,
  windowLabel,
}: {
  id: string;
  title: string;
  count: string;
  windowLabel?: string;
}) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-2">
      <div className="flex items-baseline gap-2">
        <h2 id={id} className="text-base font-semibold">
          {title}
        </h2>
        <span className="text-xs text-muted-foreground">{count}</span>
      </div>
      {windowLabel ? <Badge variant="outline">{windowLabel}</Badge> : null}
    </div>
  );
}

function UserRankingRow({
  rank,
  user,
  metrics,
  totalCost,
}: {
  rank: number;
  user: ReportUserUsage;
  metrics: ReportUsageMetrics | null;
  totalCost: string;
}) {
  return (
    <TableRow>
      <RankCell rank={rank} />
      <TableHead scope="row" className="h-auto max-w-72">
        <div className="flex min-w-0 flex-col">
          <span className="truncate font-medium" title={user.email}>
            {user.email}
          </span>
          <span className="font-mono text-xs text-muted-foreground">
            ID {user.externalUserId}
          </span>
        </div>
      </TableHead>
      <TableCell className="hidden text-right tabular-nums sm:table-cell">
        {user.keyCount}
      </TableCell>
      <UsageCells metrics={metrics} totalCost={totalCost} />
    </TableRow>
  );
}

function KeyRankingRow({
  rank,
  item,
  metrics,
  totalCost,
}: {
  rank: number;
  item: ReportKeyUsage;
  metrics: ReportUsageMetrics | null;
  totalCost: string;
}) {
  return (
    <TableRow>
      <RankCell rank={rank} />
      <TableHead scope="row" className="h-auto max-w-80">
        <div className="flex min-w-0 flex-col gap-1">
          <div className="flex min-w-0 items-center gap-2">
            <span className="truncate font-medium" title={item.name}>
              {item.name}
            </span>
            {item.status.toLowerCase() !== "active" ? (
              <Badge variant="outline">{formatKeyStatus(item.status)}</Badge>
            ) : null}
            {item.retiredAt ? <Badge variant="secondary">上游已移除</Badge> : null}
          </div>
          {item.sourceUserEmail ? (
            <span
              className="truncate text-xs text-muted-foreground"
              title={item.sourceUserEmail}
            >
              {item.sourceUserEmail} · ID {item.externalId}
            </span>
          ) : (
            <span className="font-mono text-xs text-muted-foreground">
              ID {item.externalId}
            </span>
          )}
        </div>
      </TableHead>
      <UsageCells metrics={metrics} totalCost={totalCost} />
    </TableRow>
  );
}

function RankCell({ rank }: { rank: number }) {
  return (
    <TableCell className="text-center">
      {rank <= 3 ? (
        <Badge
          variant={rank === 1 ? "default" : "secondary"}
          aria-label={`第 ${rank} 名`}
          className="min-w-7 justify-center tabular-nums"
        >
          {rank}
        </Badge>
      ) : (
        <span
          className="text-sm text-muted-foreground tabular-nums"
          aria-label={`第 ${rank} 名`}
        >
          {rank}
        </span>
      )}
    </TableCell>
  );
}

function UsageCells({
  metrics,
  totalCost,
}: {
  metrics: ReportUsageMetrics | null;
  totalCost: string;
}) {
  const share = calculateShare(metrics?.totalActualCost ?? "0", totalCost);
  return (
    <>
      <TableCell className="hidden text-right tabular-nums md:table-cell">
        {metrics ? formatCount(metrics.totalRequests) : "—"}
      </TableCell>
      <TableCell className="hidden text-right tabular-nums md:table-cell">
        {metrics ? formatCount(metrics.totalTokens) : "—"}
      </TableCell>
      <TableCell className="text-right">
        <div className="ml-auto flex w-32 flex-col gap-1.5">
          <div className="flex items-baseline justify-end gap-2 tabular-nums">
            <span
              className="font-semibold"
              title={metrics ? `$${formatCost(metrics.totalActualCost)}` : undefined}
              aria-label={
                metrics
                  ? `实际费用 ${formatCost(metrics.totalActualCost)} 美元`
                  : "无费用数据"
              }
            >
              {metrics ? formatUsd(metrics.totalActualCost) : "—"}
            </span>
            <span className="w-12 text-xs text-muted-foreground">
              {formatShare(share)}
            </span>
          </div>
          <Progress value={share} aria-label={`费用占比 ${formatShare(share)}`} />
        </div>
      </TableCell>
    </>
  );
}

function rankUsage<T>(
  items: readonly T[],
  getMetrics: (item: T) => ReportUsageMetrics | null,
) {
  return items
    .map((item) => ({ item, metrics: getMetrics(item) }))
    .sort((left, right) => compareMetrics(right.metrics, left.metrics));
}

function compareMetrics(
  left: ReportUsageMetrics | null,
  right: ReportUsageMetrics | null,
) {
  const costDifference =
    Number(left?.totalActualCost ?? 0) - Number(right?.totalActualCost ?? 0);
  if (Number.isFinite(costDifference) && costDifference !== 0) {
    return costDifference;
  }
  try {
    const requestDifference =
      BigInt(left?.totalRequests ?? 0) - BigInt(right?.totalRequests ?? 0);
    return requestDifference > 0n ? 1 : requestDifference < 0n ? -1 : 0;
  } catch {
    return 0;
  }
}

function calculateShare(value: string, total: string) {
  const numericValue = Number(value);
  const numericTotal = Number(total);
  if (
    !Number.isFinite(numericValue) ||
    !Number.isFinite(numericTotal) ||
    numericTotal <= 0
  ) {
    return 0;
  }
  return Math.min(100, Math.max(0, (numericValue / numericTotal) * 100));
}

function formatShare(value: number) {
  if (value > 0 && value < 0.1) {
    return "<0.1%";
  }
  return `${value.toFixed(1)}%`;
}

function formatKeyStatus(status: string) {
  return status.toLowerCase() === "active" ? "有效" : status;
}

function getWindowDisplayLabel(window: ReportWindowDescriptor) {
  if (window.kind === "PreviousCalendarWeek") return "上周";
  if (window.kind === "PreviousCalendarMonth") return "上月";
  return window.label;
}

function ReportDetailLoading() {
  return (
    <div
      className="mx-auto flex w-full max-w-[1440px] flex-col gap-6"
      aria-busy="true"
    >
      <Skeleton className="h-8 w-48" />
      <Skeleton className="h-28 w-full" />
      <Skeleton className="h-64 w-full" />
    </div>
  );
}
