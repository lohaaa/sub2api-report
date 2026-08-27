import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { MegaphoneIcon, RefreshCwIcon, SendIcon } from "lucide-react";
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
import {
    ApiError,
    deliverReport,
    getChannels,
    getReportDeliveries,
    retryReportDelivery,
    type Delivery,
    type DeliveryRun,
    type DeliveryRunStatus,
    type DeliveryStatus,
} from "@/lib/api-client";
import { cn } from "@/lib/utils";
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

export function ReportDeliveryPanel({
    reportId,
    reportStatus,
}: {
    reportId: string;
    reportStatus: "Complete" | "Partial";
}) {
    const queryClient = useQueryClient();
    const [selected, setSelected] = useState<string[]>([]);
    const [confirmPartial, setConfirmPartial] = useState(false);
    const channelsQuery = useQuery({
        queryKey: ["channels"],
        queryFn: ({ signal }) => getChannels(signal),
    });
    const deliveriesQuery = useQuery({
        queryKey: ["report-deliveries", reportId],
        queryFn: ({ signal }) => getReportDeliveries(reportId, signal),
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

    const enabledChannels = (channelsQuery.data ?? []).filter(
        (channel) => channel.enabled,
    );
    const runs = deliveriesQuery.data ?? [];
    const isBusy = deliverMutation.isPending || retryMutation.isPending;

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

    return (
        <section
            aria-labelledby="delivery-title"
            className="flex flex-col gap-4"
        >
            <h2 id="delivery-title" className="text-base font-semibold">
                发送报告
            </h2>
            <FormError
                message={
                    deliverMutation.error instanceof ApiError
                        ? deliverMutation.error.message
                        : channelsQuery.isError || deliveriesQuery.isError
                          ? "无法读取发送渠道或投递记录。"
                          : null
                }
            />
            {deliverMutation.isSuccess ? (
                <Alert
                    variant={
                        deliverMutation.data.status === "Succeeded"
                            ? "default"
                            : "destructive"
                    }
                >
                    <SendIcon aria-hidden="true" />
                    <AlertTitle>投递已完成</AlertTitle>
                    <AlertDescription>
                        本次运行状态：
                        {runStatusLabels[deliverMutation.data.status]}
                        。失败渠道可以只补发失败部分。
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
                            className={cn(
                                buttonVariants({ variant: "link" }),
                                "px-1",
                            )}
                            to="/channels"
                        >
                            发送渠道
                        </Link>
                        页面创建并启用渠道。
                    </AlertDescription>
                </Alert>
            ) : (
                <div className="flex flex-col gap-3 border-y py-4">
                    <fieldset className="flex flex-wrap gap-x-6 gap-y-2">
                        <legend className="mb-2 text-sm font-medium">
                            选择渠道
                        </legend>
                        {enabledChannels.map((channel) => (
                            <label
                                key={channel.id}
                                className="flex items-center gap-2 text-sm"
                            >
                                <Checkbox
                                    checked={selected.includes(channel.id)}
                                    onCheckedChange={() =>
                                        toggleSelected(channel.id)
                                    }
                                    disabled={isBusy}
                                />
                                {channel.name}
                                <span className="text-xs text-muted-foreground">
                                    （{channelTypeLabel(channel.type)}）
                                </span>
                            </label>
                        ))}
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
                                    />
                                    <span>
                                        我已知晓数据不完整，仍要发送该报告。
                                    </span>
                                </label>
                            </AlertDescription>
                        </Alert>
                    ) : null}
                    <div>
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
                            <span className="ml-3 text-xs text-muted-foreground">
                                正在依次发送，请勿关闭页面…
                            </span>
                        ) : null}
                    </div>
                </div>
            )}

            {runs.length > 0 ? (
                <div className="flex flex-col gap-4">
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
                                {run.status !== "Running" &&
                                run.status !== "Succeeded" ? (
                                    <Button
                                        variant="outline"
                                        size="sm"
                                        disabled={isBusy}
                                        onClick={() => retry(run)}
                                    >
                                        {retryMutation.isPending ? (
                                            <Spinner data-icon="inline-start" />
                                        ) : (
                                            <RefreshCwIcon data-icon="inline-start" />
                                        )}
                                        只补发失败渠道
                                    </Button>
                                ) : null}
                            </div>
                            <Table>
                                <TableCaption>分渠道投递状态</TableCaption>
                                <TableHeader>
                                    <TableRow>
                                        <TableHead scope="col">渠道</TableHead>
                                        <TableHead scope="col">类型</TableHead>
                                        <TableHead scope="col">状态</TableHead>
                                        <TableHead
                                            scope="col"
                                            className="text-right"
                                        >
                                            尝试
                                        </TableHead>
                                        <TableHead scope="col">说明</TableHead>
                                    </TableRow>
                                </TableHeader>
                                <TableBody>
                                    {run.deliveries.map((delivery) => (
                                        <DeliveryRow
                                            key={delivery.id}
                                            delivery={delivery}
                                        />
                                    ))}
                                </TableBody>
                            </Table>
                        </div>
                    ))}
                </div>
            ) : null}
        </section>
    );
}

function DeliveryRow({ delivery }: { delivery: Delivery }) {
    return (
        <TableRow>
            <TableCell className="font-medium">
                {delivery.channelName}
            </TableCell>
            <TableCell>{channelTypeLabel(delivery.channelType)}</TableCell>
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
            <TableCell className="text-right tabular-nums">
                {delivery.attempts}
            </TableCell>
            <TableCell>
                <div className="flex flex-col gap-1 text-xs">
                    {delivery.errorMessage ? (
                        <span className="text-destructive">
                            {delivery.errorMessage}
                        </span>
                    ) : null}
                    {delivery.parts.map((part) => (
                        <span
                            key={part.index}
                            className="text-muted-foreground"
                        >
                            分片 {part.index + 1}/{part.count}：
                            {part.status === "Succeeded"
                                ? "成功"
                                : part.status === "Failed"
                                  ? `失败（${part.errorCode ?? "failed"}）`
                                  : "待发送"}
                        </span>
                    ))}
                </div>
            </TableCell>
        </TableRow>
    );
}

function channelTypeLabel(type: string) {
    return type === "Email" ? "邮件" : type === "DingTalk" ? "钉钉" : "飞书";
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
