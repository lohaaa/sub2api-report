import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  EyeIcon,
  MailIcon,
  MegaphoneIcon,
  MessageSquareTextIcon,
  RefreshCwIcon,
  ShieldXIcon,
  SendIcon,
} from "lucide-react";
import { useState } from "react";
import { Link } from "react-router-dom";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button, buttonVariants } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
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
import { channelPresentations } from "@/features/channels/channel-presentation";
import {
  ApiError,
  deliverReport,
  getChannels,
  getReportDeliveries,
  retryReportDelivery,
  revokeReportDownloadGrant,
  type Delivery,
  type DeliveryPart,
  type DeliveryRun,
  type DeliveryRunStatus,
  type DeliveryStatus,
  type ReportDownloadGrant,
  type ReportDetail,
  type NotificationChannelType,
} from "@/lib/api-client";
import { cn } from "@/lib/utils";
import { ReportChannelPreviewDialog } from "./report-channel-preview-dialog";
import { formatTimestamp } from "./report-format";

const runStatusLabels: Record<DeliveryRunStatus, string> = {
  Running: "发送中",
  Succeeded: "全部成功",
  PartialFailed: "部分失败",
  Failed: "全部失败",
};

const deliveryStatusLabels: Record<DeliveryStatus, string> = {
  Pending: "待发送",
  Sending: "发送中",
  Succeeded: "成功",
  Failed: "失败",
};

const deliveryErrorLabels: Record<string, string> = {
  smtp_auth_failed: "SMTP 认证失败",
  smtp_connect_failed: "无法连接 SMTP",
  smtp_send_failed: "邮件发送失败",
  rate_limited: "平台限流",
  unavailable: "平台暂不可用",
  timeout: "发送超时",
  rejected: "平台拒绝请求",
  business_error: "平台返回业务错误",
  invalid_response: "平台响应无效",
  invalid_webhook: "Webhook 地址无效",
  channel_unavailable: "渠道已停用或不存在",
  payload_changed: "待发送内容已变化",
  outcome_unknown: "发送结果未知",
  cancelled: "发送已中断",
  internal_error: "内部发送错误",
};

export function ReportDeliveryPanel({
  reportId,
  reportStatus,
  report,
}: {
  reportId: string;
  reportStatus: "Complete" | "Partial";
  report: ReportDetail;
}) {
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState<string[]>([]);
  const [confirmPartial, setConfirmPartial] = useState(false);
  const [previewChannelType, setPreviewChannelType] =
    useState<NotificationChannelType | null>(null);
  const channelsQuery = useQuery({
    queryKey: ["channels"],
    queryFn: ({ signal }) => getChannels(signal),
  });
  const deliveriesQuery = useQuery({
    queryKey: ["report-deliveries", reportId],
    queryFn: ({ signal }) => getReportDeliveries(reportId, signal),
    refetchInterval: (query) =>
      query.state.data?.some((run) => run.status === "Running") ? 3_000 : false,
  });
  const deliverMutation = useMutation({
    mutationFn: (input: {
      channelIds: string[];
      confirmPartial: boolean;
    }) => deliverReport(reportId, input),
    onSuccess: async () => {
      setSelected([]);
      setConfirmPartial(false);
      await queryClient.invalidateQueries({
        queryKey: ["report-deliveries", reportId],
      });
    },
  });
  const retryMutation = useMutation({
    mutationFn: (runId: string) => retryReportDelivery(reportId, runId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["report-deliveries", reportId],
      });
    },
  });
  const revokeMutation = useMutation({
    mutationFn: (grantId: string) =>
      revokeReportDownloadGrant(reportId, grantId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["report-deliveries", reportId],
      });
    },
  });

  const enabledChannels = (channelsQuery.data ?? []).filter(
    (channel) => channel.enabled,
  );
  const runs = deliveriesQuery.data ?? [];
  const isBusy =
    deliverMutation.isPending ||
    retryMutation.isPending ||
    revokeMutation.isPending;

  function toggleSelected(id: string) {
    setSelected((current) =>
      current.includes(id)
        ? current.filter((item) => item !== id)
        : [...current, id],
    );
  }

  async function retry(run: DeliveryRun) {
    try {
      await retryMutation.mutateAsync(run.id);
    } catch {
      // mutation.error surfaces the failure through FormError
    }
  }

  const mutationError =
    deliverMutation.error ?? retryMutation.error ?? revokeMutation.error;

  return (
    <section aria-labelledby="delivery-title" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 id="delivery-title" className="text-base font-semibold">
          发送报告
        </h2>
        <span className="text-xs text-muted-foreground">
          邮件发送完整明细，群机器人发送摘要与可选限时链接
        </span>
      </div>
      <FormError
        message={
          mutationError instanceof ApiError
            ? mutationError.message
            : channelsQuery.isError || deliveriesQuery.isError
              ? "无法读取发送渠道或投递记录。"
              : null
        }
      />
      {deliverMutation.isSuccess ? (
        <Alert
          role="status"
          variant={
            deliverMutation.data.status === "Succeeded"
              ? "default"
              : "destructive"
          }
        >
          <SendIcon aria-hidden="true" />
          <AlertTitle>投递已完成</AlertTitle>
          <AlertDescription>
            本次运行状态：{runStatusLabels[deliverMutation.data.status]}。
            失败渠道可以只补发失败部分。
          </AlertDescription>
        </Alert>
      ) : null}
      {enabledChannels.length === 0 ? (
        <Alert>
          <MegaphoneIcon aria-hidden="true" />
          <AlertTitle>还没有可用的发送渠道</AlertTitle>
          <AlertDescription>
            请先在
            <Link
              className={cn(buttonVariants({ variant: "link" }), "px-1")}
              to="/channels"
            >
              发送渠道
            </Link>
            页面创建并启用渠道。
          </AlertDescription>
        </Alert>
      ) : (
        <div className="flex flex-col gap-4 border-y py-4">
          <fieldset className="flex flex-col gap-2">
            <legend className="text-sm font-medium">选择渠道</legend>
            <div className="divide-y rounded-lg border">
              {enabledChannels.map((channel) => {
                const presentation = channelPresentations[channel.type];
                const ChannelIcon = getChannelIcon(channel.type);
                return (
                  <div
                    key={channel.id}
                    className="grid grid-cols-[auto_minmax(0,1fr)] items-start gap-x-3 gap-y-2 px-3 py-3 sm:grid-cols-[auto_minmax(0,1fr)_auto]"
                  >
                    <Checkbox
                      id={`delivery-channel-${channel.id}`}
                      className="mt-1"
                      checked={selected.includes(channel.id)}
                      onCheckedChange={() => toggleSelected(channel.id)}
                      disabled={isBusy}
                      aria-label={`发送报告到 ${channel.name}`}
                    />
                    <label
                      htmlFor={`delivery-channel-${channel.id}`}
                      className="flex min-w-0 cursor-pointer gap-2.5"
                    >
                      <ChannelIcon
                        aria-hidden="true"
                        className="mt-0.5 size-4 shrink-0 text-muted-foreground"
                      />
                      <span className="flex min-w-0 flex-col gap-1">
                        <span className="min-w-0 text-sm font-medium">
                          {channel.name}
                          <span className="ml-2 font-normal text-muted-foreground">
                            {presentation.fullLabel}
                          </span>
                        </span>
                        <span className="text-xs text-muted-foreground">
                          {presentation.capability}
                        </span>
                      </span>
                    </label>
                    <div className="col-start-2 flex flex-wrap items-center gap-1.5 sm:col-start-auto sm:justify-end">
                      <Badge variant="outline">{presentation.contentLabel}</Badge>
                      <Badge
                        variant={presentation.hasAttachment ? "secondary" : "outline"}
                      >
                        {presentation.attachmentLabel}
                      </Badge>
                      {channel.lastTestSucceeded === false ? (
                        <Badge variant="destructive">上次测试失败</Badge>
                      ) : channel.lastTestSucceeded === null ? (
                        <Badge variant="outline">未测试</Badge>
                      ) : null}
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        aria-label={`预览${channel.name}的${presentation.shortLabel}消息`}
                        onClick={() => setPreviewChannelType(channel.type)}
                      >
                        <EyeIcon data-icon="inline-start" />
                        预览
                      </Button>
                    </div>
                  </div>
                );
              })}
            </div>
          </fieldset>
          {reportStatus === "Partial" ? (
            <Alert variant="destructive">
              <AlertTitle>该报告为部分完成</AlertTitle>
              <AlertDescription>
                <label className="flex items-start gap-2">
                  <Checkbox
                    className="mt-0.5"
                    checked={confirmPartial}
                    onCheckedChange={(checked) =>
                      setConfirmPartial(checked === true)
                    }
                    disabled={isBusy}
                    aria-label="确认发送数据不完整的报告"
                  />
                  <span>我已知晓数据不完整，仍要发送该报告。</span>
                </label>
              </AlertDescription>
            </Alert>
          ) : null}
          <div className="flex flex-col items-start gap-2 sm:flex-row sm:items-center">
            <Button
              onClick={() =>
                deliverMutation.mutate({
                  channelIds: selected,
                  confirmPartial,
                })
              }
              disabled={
                isBusy ||
                selected.length === 0 ||
                (reportStatus === "Partial" && !confirmPartial)
              }
            >
              {deliverMutation.isPending ? (
                <Spinner data-icon="inline-start" />
              ) : (
                <SendIcon data-icon="inline-start" />
              )}
              发送给选定渠道（{selected.length}）
            </Button>
            {deliverMutation.isPending ? (
              <span className="text-xs text-muted-foreground">
                正在依次发送，请勿关闭页面…
              </span>
            ) : selected.length === 0 ? (
              <span className="text-xs text-muted-foreground">
                选择至少一个渠道后发送
              </span>
            ) : null}
          </div>
        </div>
      )}

      {previewChannelType ? (
        <ReportChannelPreviewDialog
          open
          channelType={previewChannelType}
          report={report}
          onOpenChange={(open) => {
            if (!open) setPreviewChannelType(null);
          }}
        />
      ) : null}
      {runs.length > 0 ? (
        <div className="flex flex-col gap-5">
          {runs.map((run) => (
            <div key={run.id} className="flex flex-col gap-2">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={runBadgeVariant(run.status)}>
                  {runStatusLabels[run.status]}
                </Badge>
                <span className="text-xs text-muted-foreground">
                  开始 {formatTimestamp(run.startedAt)}
                  {run.completedAt
                    ? ` · 结束 ${formatTimestamp(run.completedAt)}`
                    : ""}
                </span>
                {run.status !== "Running" && run.status !== "Succeeded" ? (
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={isBusy}
                    onClick={() => retry(run)}
                    title="只补发失败渠道和失败消息，不重复已成功部分"
                  >
                    {retryMutation.isPending ? (
                      <Spinner data-icon="inline-start" />
                    ) : (
                      <RefreshCwIcon data-icon="inline-start" />
                    )}
                    补发失败部分
                  </Button>
                ) : null}
              </div>
              <div className="overflow-hidden rounded-lg border">
                <Table>
                  <TableCaption>分渠道投递状态</TableCaption>
                  <TableHeader>
                    <TableRow>
                      <TableHead scope="col">渠道</TableHead>
                      <TableHead scope="col" className="hidden md:table-cell">
                        类型
                      </TableHead>
                      <TableHead scope="col">状态</TableHead>
                      <TableHead
                        scope="col"
                        className="hidden text-right sm:table-cell"
                      >
                        尝试
                      </TableHead>
                      <TableHead scope="col">发送内容</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {run.deliveries.map((delivery) => (
                      <DeliveryRow
                        key={delivery.id}
                        delivery={delivery}
                        revoking={revokeMutation.isPending}
                        onRevoke={(grantId) => revokeMutation.mutate(grantId)}
                      />
                    ))}
                  </TableBody>
                </Table>
              </div>
            </div>
          ))}
        </div>
      ) : null}
    </section>
  );
}

function DeliveryRow({
  delivery,
  revoking,
  onRevoke,
}: {
  delivery: Delivery;
  revoking: boolean;
  onRevoke: (grantId: string) => void;
}) {
  const presentation = channelPresentations[delivery.channelType];
  return (
    <TableRow>
      <TableCell className="font-medium">
        <div className="flex min-w-0 flex-col gap-1">
          <span>{delivery.channelName}</span>
          <span className="text-xs font-normal text-muted-foreground md:hidden">
            {presentation.fullLabel}
          </span>
        </div>
      </TableCell>
      <TableCell className="hidden md:table-cell">
        {presentation.fullLabel}
      </TableCell>
      <TableCell>
        <div className="flex flex-col gap-1">
          <Badge variant={deliveryBadgeVariant(delivery.status)}>
            {deliveryStatusLabels[delivery.status]}
          </Badge>
          {delivery.sentAt ? (
            <span className="text-xs text-muted-foreground">
              {formatTimestamp(delivery.sentAt)}
            </span>
          ) : null}
        </div>
      </TableCell>
      <TableCell className="hidden text-right tabular-nums sm:table-cell">
        {delivery.attempts}
      </TableCell>
      <TableCell className="whitespace-normal">
        <div className="flex min-w-48 flex-col gap-1.5 text-xs">
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="font-medium">{presentation.contentLabel}</span>
            <Badge
              variant={presentation.hasAttachment ? "secondary" : "outline"}
            >
              {presentation.attachmentLabel}
            </Badge>
          </div>
          {delivery.errorCode ? (
            <span
              className="text-destructive"
              title={delivery.errorMessage ?? delivery.errorCode}
            >
              {deliveryErrorLabels[delivery.errorCode] ?? "发送失败"}
              {delivery.errorMessage ? `：${delivery.errorMessage}` : ""}
            </span>
          ) : null}
          {delivery.downloadGrant ? (
            <div className="flex flex-wrap items-center gap-1.5">
              <Badge
                variant={
                  delivery.downloadGrant.revokedAt ? "destructive" : "outline"
                }
              >
                {downloadGrantLabel(delivery.downloadGrant)}
              </Badge>
              <span className="text-muted-foreground">
                已下载 {delivery.downloadGrant.downloadCount}
                {delivery.downloadGrant.maxDownloads === null
                  ? " 次（不限制）"
                  : `/${delivery.downloadGrant.maxDownloads} 次`}
              </span>
              {delivery.downloadGrant.revokedAt === null ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  disabled={revoking}
                  onClick={() => onRevoke(delivery.downloadGrant!.id)}
                >
                  {revoking ? (
                    <Spinner data-icon="inline-start" />
                  ) : (
                    <ShieldXIcon data-icon="inline-start" />
                  )}
                  撤销链接
                </Button>
              ) : null}
            </div>
          ) : delivery.channelType !== "Email" ? (
            <span className="text-muted-foreground">
              未配置外部访问地址，本次消息不含下载链接
            </span>
          ) : null}
          {delivery.channelType === "Email" ? (
            <span className="text-muted-foreground">
              {describeEmailPart(delivery.parts[0])}
            </span>
          ) : (
            delivery.parts.map((part) => (
              <span key={part.index} className="text-muted-foreground">
                消息 {part.index + 1}/{part.count}：{partStatusLabel(part)}
              </span>
            ))
          )}
        </div>
      </TableCell>
    </TableRow>
  );
}

function downloadGrantLabel(grant: ReportDownloadGrant) {
  if (grant.revokedAt) return "已撤销";
  if (!grant.expiresAt) return "发送成功后生效";
  if (grant.maxDownloads !== null && grant.downloadCount >= grant.maxDownloads) {
    return "已达次数上限";
  }
  if (new Date(grant.expiresAt).getTime() <= Date.now()) return "已过期";
  return `有效至 ${formatTimestamp(grant.expiresAt)}`;
}


function describeEmailPart(part: DeliveryPart | undefined) {
  if (!part || part.status === "Pending") {
    return "邮件待发送，将包含 XLSX 附件";
  }
  return part.status === "Succeeded"
    ? "邮件 1 封已发送，包含 XLSX 附件 1 个"
    : `邮件发送失败（${part.errorCode ?? "failed"}）`;
}

function partStatusLabel(part: DeliveryPart) {
  return part.status === "Succeeded"
    ? "成功"
    : part.status === "Failed"
      ? `失败（${part.errorCode ?? "failed"}）`
      : "待发送";
}

function getChannelIcon(type: NotificationChannelType) {
  return type === "Email" ? MailIcon : MessageSquareTextIcon;
}

function runBadgeVariant(status: DeliveryRunStatus) {
  return status === "Succeeded"
    ? "secondary"
    : status === "Running"
      ? "outline"
      : "destructive";
}

function deliveryBadgeVariant(status: DeliveryStatus) {
  return status === "Succeeded"
    ? "secondary"
    : status === "Failed"
      ? "destructive"
      : "outline";
}
