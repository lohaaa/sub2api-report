import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertCircleIcon,
  CalendarClockIcon,
  CheckCircle2Icon,
  EyeIcon,
  PlayIcon,
  RotateCcwIcon,
} from "lucide-react";
import { Controller, useForm } from "react-hook-form";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { z } from "zod";
import { PageHeader } from "@/components/layout/page-header";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Spinner } from "@/components/ui/spinner";
import { Switch } from "@/components/ui/switch";
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
import { formatDate, formatTimestamp } from "@/features/reports/report-format";
import {
  ApiError,
  createDefaultReportWindowSpecs,
  getChannels,
  getReportSchedule,
  getReportTaskRuns,
  reportWindowKeys,
  retryReportTaskRun,
  runReportScheduleNow,
  updateReportSchedule,
  type ReportTaskRun,
  type ReportTaskStatus,
  type ReportTaskTrigger,
  type ReportWindowSpec,
} from "@/lib/api-client";

const scheduleSchema = z.object({
  enabled: z.boolean(),
  dayOfMonth: z.number().int().min(1, "日期不能小于 1").max(28, "日期不能大于 28"),
  localTime: z.string().regex(/^(?:[01]\d|2[0-3]):[0-5]\d$/, "请输入有效时间"),
  timezone: z.string().trim().min(1, "请输入 IANA 时区").max(100),
});
type ScheduleValues = z.infer<typeof scheduleSchema>;

const builtinWindowOptions = [
  { key: reportWindowKeys.rollingSevenDays, label: "滚动 7 天" },
  { key: reportWindowKeys.rollingThirtyDays, label: "滚动 30 天" },
  { key: reportWindowKeys.previousCalendarWeek, label: "上一完整自然周" },
  { key: reportWindowKeys.previousCalendarMonth, label: "上一完整自然月" },
] as const;

function windowSummary(specs: readonly ReportWindowSpec[]) {
  if (specs.length === 0) {
    return "未选择";
  }
  return specs
    .map(
      (spec) =>
        builtinWindowOptions.find((option) => option.key === spec.key)?.label ??
        spec.key,
    )
    .join("、");
}

const activeStatuses = new Set<ReportTaskStatus>([
  "Queued",
  "Collecting",
  "Rendering",
  "Delivering",
]);

export function SchedulePage() {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [confirmingRun, setConfirmingRun] = useState<ReportTaskRun | null>(null);
  const [windowSpecs, setWindowSpecs] = useState<ReportWindowSpec[]>([]);
  const [windowError, setWindowError] = useState<string | null>(null);
  const scheduleQuery = useQuery({
    queryKey: ["report-schedule"],
    queryFn: ({ signal }) => getReportSchedule(signal),
  });
  const runsQuery = useQuery({
    queryKey: ["report-task-runs", page],
    queryFn: ({ signal }) => getReportTaskRuns(page, signal),
    refetchInterval: (query) =>
      query.state.data?.items.some((run) => activeStatuses.has(run.status))
        ? 5_000
        : false,
  });
  const channelsQuery = useQuery({
    queryKey: ["channels"],
    queryFn: ({ signal }) => getChannels(signal),
  });
  const form = useForm<ScheduleValues>({
    resolver: zodResolver(scheduleSchema),
    defaultValues: {
      enabled: false,
      dayOfMonth: 1,
      localTime: "09:00",
      timezone: "Asia/Shanghai",
    },
  });
  useEffect(() => {
    if (scheduleQuery.data) {
      form.reset({
        enabled: scheduleQuery.data.enabled,
        dayOfMonth: scheduleQuery.data.dayOfMonth,
        localTime: scheduleQuery.data.localTime,
        timezone: scheduleQuery.data.timezone,
      });
      setWindowSpecs(scheduleQuery.data.windows ?? []);
    }
  }, [form, scheduleQuery.data]);

  const saveMutation = useMutation({
    mutationFn: (values: ScheduleValues) => updateReportSchedule({
      ...values,
      windows: windowSpecs.filter((spec) => spec.kind !== "CustomRange"),
      revision: scheduleQuery.data?.revision ?? 0,
    }),
    onSuccess: (schedule) => {
      queryClient.setQueryData(["report-schedule"], schedule);
      form.reset({
        enabled: schedule.enabled,
        dayOfMonth: schedule.dayOfMonth,
        localTime: schedule.localTime,
        timezone: schedule.timezone,
      });
      setWindowSpecs(schedule.windows ?? []);
    },
  });
  const runMutation = useMutation({
    mutationFn: runReportScheduleNow,
    onSuccess: invalidateExecutionData,
  });
  const retryMutation = useMutation({
    mutationFn: ({ runId, confirm }: { runId: string; confirm: boolean }) =>
      retryReportTaskRun(runId, confirm),
    onSuccess: async () => {
      setConfirmingRun(null);
      await invalidateExecutionData();
    },
  });

  function toggleWindowSpec(key: string, checked: boolean) {
    setWindowError(null);
    setWindowSpecs((current) =>
      checked
        ? [
            ...current,
            ...createDefaultReportWindowSpecs().filter(
              (spec) => spec.key === key && !current.some((item) => item.key === key),
            ),
          ]
        : current.filter((spec) => spec.key !== key),
    );
  }

  async function invalidateExecutionData() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["report-task-runs"] }),
      queryClient.invalidateQueries({ queryKey: ["reports"] }),
      queryClient.invalidateQueries({ queryKey: ["report-generations"] }),
      queryClient.invalidateQueries({ queryKey: ["report-schedule"] }),
    ]);
  }

  function requestRetry(run: ReportTaskRun) {
    if (run.hasOutcomeUnknown) {
      setConfirmingRun(run);
      return;
    }

    retryMutation.mutate({ runId: run.id, confirm: false });
  }

  const serverWindowKeys = scheduleQuery.data?.windows?.map((spec) => spec.key) ?? [];
  const windowsDirty =
    windowSpecs.map((spec) => spec.key).join("\u0000") !== serverWindowKeys.join("\u0000") ||
    windowSpecs.length !== serverWindowKeys.length;
  const enabledChannels = channelsQuery.data?.filter((channel) => channel.enabled).length ?? 0;
  const error = saveMutation.error ?? runMutation.error ?? retryMutation.error;
  const errorMessage = error instanceof ApiError
    ? error.message
    : scheduleQuery.isError || runsQuery.isError
      ? "无法读取计划任务状态。"
      : null;

  return (
    <div className="mx-auto flex w-full max-w-[1440px] flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <PageHeader title="计划任务" description="月报计划、执行阶段和失败重试" />
        <Button
          disabled={runMutation.isPending}
          onClick={() => runMutation.mutate()}
        >
          {runMutation.isPending
            ? <Spinner data-icon="inline-start" />
            : <PlayIcon data-icon="inline-start" />}
          立即运行
        </Button>
      </div>

      <FormError message={errorMessage} />
      {scheduleQuery.data && !scheduleQuery.data.synchronized ? (
        <Alert variant="destructive">
          <AlertCircleIcon aria-hidden="true" />
          <AlertTitle>计划尚未应用</AlertTitle>
          <AlertDescription>
            持久化 trigger 状态异常：{scheduleQuery.data.synchronizationErrorCode ?? "unknown"}
          </AlertDescription>
        </Alert>
      ) : null}

      <section aria-labelledby="schedule-settings-title" className="border-y py-5">
        <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 id="schedule-settings-title" className="text-base font-semibold">月报计划</h2>
            <p className="text-sm text-muted-foreground">
              {scheduleQuery.data?.nextRunAt
                ? `下次运行 ${formatTimestamp(scheduleQuery.data.nextRunAt)}`
                : "当前没有待运行的自动任务"}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Badge variant="outline">{enabledChannels} 个启用渠道</Badge>
            <Badge variant={scheduleQuery.data?.enabled ? "secondary" : "outline"}>
              {scheduleQuery.data?.enabled ? "已启用" : "已停用"}
            </Badge>
          </div>
        </div>

        {scheduleQuery.isPending ? (
          <div className="grid gap-3 sm:grid-cols-3" aria-busy="true">
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-16 w-full" />
            <Skeleton className="h-16 w-full" />
          </div>
        ) : scheduleQuery.data ? (
          <form
            className="flex flex-col gap-5"
            noValidate
            onSubmit={form.handleSubmit((values) => {
              if (windowSpecs.length === 0) {
                setWindowError("至少选择一个统计窗口");
                return;
              }
              setWindowError(null);
              saveMutation.mutate(values);
            })}
          >
            <FieldGroup className="sm:grid sm:grid-cols-3">
              <Field data-invalid={Boolean(form.formState.errors.dayOfMonth)}>
                <FieldLabel htmlFor="schedule-day">每月日期</FieldLabel>
                <Input
                  id="schedule-day"
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={28}
                  aria-invalid={Boolean(form.formState.errors.dayOfMonth)}
                  {...form.register("dayOfMonth", { valueAsNumber: true })}
                />
                <FieldError errors={[form.formState.errors.dayOfMonth]} />
              </Field>
              <Field data-invalid={Boolean(form.formState.errors.localTime)}>
                <FieldLabel htmlFor="schedule-time">运行时间</FieldLabel>
                <Input
                  id="schedule-time"
                  type="time"
                  aria-invalid={Boolean(form.formState.errors.localTime)}
                  {...form.register("localTime")}
                />
                <FieldError errors={[form.formState.errors.localTime]} />
              </Field>
              <Field data-invalid={Boolean(form.formState.errors.timezone)}>
                <FieldLabel htmlFor="schedule-timezone">时区</FieldLabel>
                <Input
                  id="schedule-timezone"
                  autoComplete="off"
                  aria-invalid={Boolean(form.formState.errors.timezone)}
                  {...form.register("timezone")}
                />
                <FieldError errors={[form.formState.errors.timezone]} />
              </Field>
              <Controller
                control={form.control}
                name="enabled"
                render={({ field }) => (
                  <Field orientation="horizontal" className="sm:col-span-3">
                    <FieldContent>
                      <FieldLabel htmlFor="schedule-enabled">启用自动月报</FieldLabel>
                      <FieldDescription>运行时使用全部已启用发送渠道</FieldDescription>
                    </FieldContent>
                    <Switch
                      id="schedule-enabled"
                      checked={field.value}
                      onCheckedChange={field.onChange}
                    />
                  </Field>
                )}
              />
            </FieldGroup>
            <FieldSet>
              <FieldLegend>统计窗口</FieldLegend>
              <FieldGroup className="sm:grid sm:grid-cols-2 sm:gap-x-6 sm:gap-y-2">
                {builtinWindowOptions.map((option) => {
                  const id = `schedule-window-${option.key}`;
                  return (
                    <Field key={option.key} orientation="horizontal">
                      <Checkbox
                        id={id}
                        checked={windowSpecs.some((spec) => spec.key === option.key)}
                        onCheckedChange={(checked) =>
                          toggleWindowSpec(option.key, checked === true)
                        }
                      />
                      <FieldLabel htmlFor={id} className="font-normal">
                        {option.label}
                      </FieldLabel>
                    </Field>
                  );
                })}
              </FieldGroup>
              <FieldDescription>
                当前统计窗口：{windowSummary(windowSpecs)}；计划任务不支持自定义区间。
              </FieldDescription>
              {windowError ? <FieldError>{windowError}</FieldError> : null}
            </FieldSet>
            <div className="flex flex-wrap items-center gap-3">
              <Button
                type="submit"
                disabled={
                  saveMutation.isPending ||
                  (!form.formState.isDirty && !windowsDirty)
                }
              >
                {saveMutation.isPending ? <Spinner data-icon="inline-start" /> : null}
                保存计划
              </Button>
              {saveMutation.isSuccess ? (
                <span className="flex items-center gap-1 text-sm text-muted-foreground">
                  <CheckCircle2Icon aria-hidden="true" />
                  revision {saveMutation.data.revision}
                </span>
              ) : null}
            </div>
          </form>
        ) : null}
      </section>

      <section aria-labelledby="task-runs-title">
        <div className="mb-3 flex items-center justify-between gap-3">
          <div>
            <h2 id="task-runs-title" className="text-base font-semibold">执行记录</h2>
            <p className="text-sm text-muted-foreground">计划、立即运行和重试尝试</p>
          </div>
          <Badge variant="secondary">{runsQuery.data?.total ?? 0} 条</Badge>
        </div>
        <div className="overflow-x-auto border-y">
          <Table>
            <TableCaption>每次尝试保留独立状态和失败原因</TableCaption>
            <TableHeader>
              <TableRow>
                <TableHead scope="col">开始时间</TableHead>
                <TableHead scope="col">触发 / 尝试</TableHead>
                <TableHead scope="col">状态</TableHead>
                <TableHead scope="col">截止日</TableHead>
                <TableHead scope="col">渠道结果</TableHead>
                <TableHead scope="col">错误</TableHead>
                <TableHead scope="col"><span className="sr-only">操作</span></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {runsQuery.isPending ? (
                Array.from({ length: 4 }, (_, index) => (
                  <TableRow key={index}>
                    <TableCell colSpan={7}><Skeleton className="h-8 w-full" /></TableCell>
                  </TableRow>
                ))
              ) : runsQuery.data?.items.length ? (
                runsQuery.data.items.map((run) => (
                  <TableRow key={run.id}>
                    <TableCell className="whitespace-nowrap">{formatTimestamp(run.startedAt)}</TableCell>
                    <TableCell>
                      <div className="flex flex-col">
                        <span>{triggerLabels[run.trigger]}</span>
                        <span className="text-xs text-muted-foreground">第 {run.attempt} 次</span>
                      </div>
                    </TableCell>
                    <TableCell><TaskStatusBadge status={run.status} /></TableCell>
                    <TableCell className="whitespace-nowrap">
                      {run.periodEnd ? formatDate(run.periodEnd) : "—"}
                    </TableCell>
                    <TableCell className="whitespace-nowrap tabular-nums">
                      {run.deliveryCount > 0
                        ? `${run.succeededDeliveryCount} 成功 / ${run.failedDeliveryCount} 失败`
                        : "—"}
                    </TableCell>
                    <TableCell className="max-w-64">
                      {run.errorMessage ?? run.errorCode ?? "—"}
                    </TableCell>
                    <TableCell>
                      <div className="flex justify-end gap-1">
                        {run.reportId ? (
                          <Link
                            className={buttonVariants({ variant: "ghost", size: "icon-sm" })}
                            to={`/reports/${run.reportId}`}
                            title="查看报告"
                            aria-label="查看任务报告"
                          >
                            <EyeIcon />
                          </Link>
                        ) : null}
                        {run.canRetry ? (
                          <Button
                            variant="outline"
                            size="sm"
                            disabled={retryMutation.isPending}
                            onClick={() => requestRetry(run)}
                          >
                            <RotateCcwIcon data-icon="inline-start" />
                            重试
                          </Button>
                        ) : null}
                      </div>
                    </TableCell>
                  </TableRow>
                ))
              ) : (
                <TableRow>
                  <TableCell colSpan={7} className="h-24 text-center text-muted-foreground">
                    <CalendarClockIcon className="mx-auto mb-2" aria-hidden="true" />
                    暂无任务执行记录
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      </section>

      {runsQuery.data && runsQuery.data.pages > 1 ? (
        <nav className="flex items-center justify-end gap-2" aria-label="任务执行记录分页">
          <Button
            variant="outline"
            size="sm"
            disabled={page <= 1}
            onClick={() => setPage((value) => Math.max(1, value - 1))}
          >
            上一页
          </Button>
          <span className="text-sm text-muted-foreground">
            {runsQuery.data.page} / {runsQuery.data.pages}
          </span>
          <Button
            variant="outline"
            size="sm"
            disabled={page >= runsQuery.data.pages}
            onClick={() => setPage((value) => value + 1)}
          >
            下一页
          </Button>
        </nav>
      ) : null}

      <Dialog open={confirmingRun !== null} onOpenChange={(open) => !open && setConfirmingRun(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>确认重试未知发送结果</DialogTitle>
            <DialogDescription>
              上次执行中存在已发出但未能记录响应的渠道，重试可能产生重复消息。
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmingRun(null)}>取消</Button>
            <Button
              disabled={retryMutation.isPending}
              onClick={() => confirmingRun && retryMutation.mutate({
                runId: confirmingRun.id,
                confirm: true,
              })}
            >
              {retryMutation.isPending ? <Spinner data-icon="inline-start" /> : null}
              确认重试
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

const triggerLabels: Record<ReportTaskTrigger, string> = {
  Scheduled: "自动计划",
  ManualScheduled: "立即运行",
  Retry: "失败重试",
};

const statusLabels: Record<ReportTaskStatus, string> = {
  Queued: "排队中",
  Collecting: "采集中",
  Rendering: "生成快照",
  Delivering: "发送中",
  Succeeded: "成功",
  PartialFailed: "部分失败",
  Failed: "失败",
};

function TaskStatusBadge({ status }: { status: ReportTaskStatus }) {
  const terminalSuccess = status === "Succeeded";
  const active = activeStatuses.has(status);
  return (
    <Badge variant={terminalSuccess ? "secondary" : "outline"}>
      {active ? <Spinner data-icon="inline-start" /> : null}
      {statusLabels[status]}
    </Badge>
  );
}
