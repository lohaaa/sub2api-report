import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2Icon } from "lucide-react";
import { useEffect } from "react";
import { Controller, useForm, useWatch } from "react-hook-form";
import { z } from "zod";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import {
  Field,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Spinner } from "@/components/ui/spinner";
import { Switch } from "@/components/ui/switch";
import { FormError } from "@/features/auth/form-error";
import {
  ApiError,
  getSystemSettings,
  updateSystemSettings,
} from "@/lib/api-client";

function isValidReportExternalBaseUrl(value: string) {
  if (value === "") return true;
  try {
    const url = new URL(value);
    return (url.protocol === "http:" || url.protocol === "https:") &&
      url.hostname.length > 0 &&
      url.username === "" &&
      url.password === "" &&
      url.search === "" &&
      url.hash === "";
  } catch {
    return false;
  }
}


const schema = z
  .object({
    timezone: z.string().trim().min(1, "请输入 IANA 时区").max(100),
    logLevel: z.enum([
      "Verbose",
      "Debug",
      "Information",
      "Warning",
      "Error",
      "Fatal",
    ]),
    reportConcurrency: z.number().int().min(1).max(10),
    reportRetentionMonths: z.number().int().min(1).max(120),
    backupRetentionCount: z.number().int().min(1).max(100),
    reportExternalBaseUrl: z
      .string()
      .trim()
      .max(2048)
      .refine(
        isValidReportExternalBaseUrl,
        "请输入不含查询参数的 HTTP 或 HTTPS 外部地址",
      ),
    downloadLifetimeValue: z.number().int().min(1).max(720),
    downloadLifetimeUnit: z.enum(["hours", "days"]),
    downloadUnlimited: z.boolean(),
    downloadMaxDownloads: z.number().int().min(1).max(10_000),
  })
  .superRefine((values, context) => {
    const hours =
      values.downloadLifetimeValue *
      (values.downloadLifetimeUnit === "days" ? 24 : 1);
    if (hours > 720) {
      context.addIssue({
        code: "custom",
        path: ["downloadLifetimeValue"],
        message: "有效期不能超过 30 天",
      });
    }
  });

type Values = z.infer<typeof schema>;

const logLevels = [
  { value: "Verbose", label: "详细" },
  { value: "Debug", label: "调试" },
  { value: "Information", label: "信息" },
  { value: "Warning", label: "警告" },
  { value: "Error", label: "错误" },
  { value: "Fatal", label: "致命" },
] as const;

export function SystemSettingsForm() {
  const queryClient = useQueryClient();
  const settingsQuery = useQuery({
    queryKey: ["system-settings"],
    queryFn: ({ signal }) => getSystemSettings(signal),
  });
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      timezone: "",
      logLevel: "Information",
      reportConcurrency: 4,
      reportRetentionMonths: 12,
      backupRetentionCount: 10,
      reportExternalBaseUrl: "",
      downloadLifetimeValue: 24,
      downloadLifetimeUnit: "hours",
      downloadUnlimited: true,
      downloadMaxDownloads: 20,
    },
  });
  const downloadUnlimited = useWatch({
    control: form.control,
    name: "downloadUnlimited",
  });
  useEffect(() => {
    if (settingsQuery.data) {
      const hours = settingsQuery.data.reportDownloadLinkHours;
      const useDays = hours % 24 === 0;
      form.reset({
        timezone: settingsQuery.data.timezone,
        logLevel: settingsQuery.data.logLevel as Values["logLevel"],
        reportConcurrency: settingsQuery.data.reportConcurrency,
        reportRetentionMonths: settingsQuery.data.reportRetentionMonths,
        backupRetentionCount: settingsQuery.data.backupRetentionCount,
        reportExternalBaseUrl: settingsQuery.data.reportExternalBaseUrl ?? "",
        downloadLifetimeValue: useDays ? hours / 24 : hours,
        downloadLifetimeUnit: useDays ? "days" : "hours",
        downloadUnlimited:
          settingsQuery.data.reportDownloadMaxDownloads === null,
        downloadMaxDownloads:
          settingsQuery.data.reportDownloadMaxDownloads ?? 20,
      });
    }
  }, [form, settingsQuery.data]);
  useEffect(() => {
    if (
      !settingsQuery.isPending &&
      window.location.hash === "#report-download-settings"
    ) {
      const target = document.getElementById("report-download-settings");
      if (target && typeof target.scrollIntoView === "function") {
        target.scrollIntoView({ block: "start" });
      }
    }
  }, [settingsQuery.isPending]);
  const mutation = useMutation({
    mutationFn: (values: Values) =>
      updateSystemSettings({
        timezone: values.timezone,
        logLevel: values.logLevel,
        reportConcurrency: values.reportConcurrency,
        reportRetentionMonths: values.reportRetentionMonths,
        backupRetentionCount: values.backupRetentionCount,
        reportExternalBaseUrl: values.reportExternalBaseUrl || null,
        reportDownloadLinkHours:
          values.downloadLifetimeValue *
          (values.downloadLifetimeUnit === "days" ? 24 : 1),
        reportDownloadMaxDownloads: values.downloadUnlimited
          ? null
          : values.downloadMaxDownloads,
        revision: settingsQuery.data?.revision ?? 0,
      }),
    onSuccess: (settings) => {
      queryClient.setQueryData(["system-settings"], settings);
    },
  });

  if (settingsQuery.isPending) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <Spinner />
        加载设置
      </div>
    );
  }
  if (settingsQuery.isError) {
    return <FormError message="无法读取系统设置。" />;
  }

  return (
    <form
      className="flex flex-col gap-6"
      onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
      noValidate
    >
      <FormError
        message={mutation.error instanceof ApiError ? mutation.error.message : null}
      />
      {mutation.isSuccess ? (
        <Alert>
          <CheckCircle2Icon aria-hidden="true" />
          <AlertTitle>设置已保存</AlertTitle>
          <AlertDescription>
            新配置已写入 revision {mutation.data.revision}。
          </AlertDescription>
        </Alert>
      ) : null}
      <FieldGroup className="sm:grid sm:grid-cols-2">
        <Field data-invalid={Boolean(form.formState.errors.timezone)}>
          <FieldLabel htmlFor="timezone">默认时区</FieldLabel>
          <Input
            id="timezone"
            autoComplete="off"
            required
            aria-invalid={Boolean(form.formState.errors.timezone)}
            {...form.register("timezone")}
          />
          <FieldError errors={[form.formState.errors.timezone]} />
        </Field>
        <Controller
          control={form.control}
          name="logLevel"
          render={({ field, fieldState }) => (
            <Field data-invalid={Boolean(fieldState.error)}>
              <FieldLabel htmlFor="log-level">日志级别</FieldLabel>
              <Select
                items={logLevels}
                value={field.value}
                onValueChange={field.onChange}
              >
                <SelectTrigger
                  id="log-level"
                  className="w-full"
                  aria-invalid={Boolean(fieldState.error)}
                >
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {logLevels.map((item) => (
                      <SelectItem key={item.value} value={item.value}>
                        {item.label}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
              <FieldError errors={[fieldState.error]} />
            </Field>
          )}
        />
        <Field data-invalid={Boolean(form.formState.errors.reportConcurrency)}>
          <FieldLabel htmlFor="report-concurrency">报告采集并发数</FieldLabel>
          <Input
            id="report-concurrency"
            type="number"
            inputMode="numeric"
            min={1}
            max={10}
            required
            aria-invalid={Boolean(form.formState.errors.reportConcurrency)}
            {...form.register("reportConcurrency", { valueAsNumber: true })}
          />
          <FieldError errors={[form.formState.errors.reportConcurrency]} />
        </Field>
        <Field data-invalid={Boolean(form.formState.errors.reportRetentionMonths)}>
          <FieldLabel htmlFor="report-retention">报告保留月数</FieldLabel>
          <Input
            id="report-retention"
            type="number"
            inputMode="numeric"
            min={1}
            max={120}
            required
            aria-invalid={Boolean(form.formState.errors.reportRetentionMonths)}
            {...form.register("reportRetentionMonths", { valueAsNumber: true })}
          />
          <FieldError errors={[form.formState.errors.reportRetentionMonths]} />
        </Field>
        <Field data-invalid={Boolean(form.formState.errors.backupRetentionCount)}>
          <FieldLabel htmlFor="backup-retention">备份保留数量</FieldLabel>
          <Input
            id="backup-retention"
            type="number"
            inputMode="numeric"
            min={1}
            max={100}
            required
            aria-invalid={Boolean(form.formState.errors.backupRetentionCount)}
            {...form.register("backupRetentionCount", { valueAsNumber: true })}
          />
          <FieldError errors={[form.formState.errors.backupRetentionCount]} />
        </Field>
      </FieldGroup>

      <FieldSet
        id="report-download-settings"
        className="scroll-mt-20 border-y py-5"
      >
        <FieldLegend>群报告下载授权</FieldLegend>
        <FieldDescription>
          钉钉和飞书消息使用限时链接提供 XLSX 工作簿；每条投递冻结当时的期限和次数限制。
        </FieldDescription>
        <FieldGroup className="mt-4 sm:grid sm:grid-cols-2">
          <Field
            className="sm:col-span-2"
            data-invalid={Boolean(form.formState.errors.reportExternalBaseUrl)}
          >
            <FieldLabel htmlFor="report-external-base-url">
              外部访问地址
            </FieldLabel>
            <Input
              id="report-external-base-url"
              type="url"
              inputMode="url"
              placeholder="http://127.0.0.1:5173"
              autoComplete="url"
              aria-invalid={Boolean(
                form.formState.errors.reportExternalBaseUrl,
              )}
              {...form.register("reportExternalBaseUrl")}
            />
            <FieldDescription>
              留空时群消息不生成下载链接；支持 HTTP/HTTPS，生产环境推荐 HTTPS。
            </FieldDescription>
            <FieldError
              errors={[form.formState.errors.reportExternalBaseUrl]}
            />
          </Field>
          <Field
            data-invalid={Boolean(form.formState.errors.downloadLifetimeValue)}
          >
            <FieldLabel htmlFor="download-lifetime">链接有效期</FieldLabel>
            <div className="grid grid-cols-[minmax(0,1fr)_8rem] gap-2">
              <Input
                id="download-lifetime"
                type="number"
                inputMode="numeric"
                min={1}
                required
                aria-invalid={Boolean(
                  form.formState.errors.downloadLifetimeValue,
                )}
                {...form.register("downloadLifetimeValue", {
                  valueAsNumber: true,
                })}
              />
              <Controller
                control={form.control}
                name="downloadLifetimeUnit"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger aria-label="有效期单位">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="hours">小时</SelectItem>
                        <SelectItem value="days">天</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                )}
              />
            </div>
            <FieldDescription>最短 1 小时，最长 30 天。</FieldDescription>
            <FieldError
              errors={[form.formState.errors.downloadLifetimeValue]}
            />
          </Field>
          <Field
            data-invalid={Boolean(form.formState.errors.downloadMaxDownloads)}
          >
            <div className="flex items-center justify-between gap-3">
              <FieldLabel htmlFor="download-unlimited">下载次数</FieldLabel>
              <Controller
                control={form.control}
                name="downloadUnlimited"
                render={({ field }) => (
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-muted-foreground">不限制</span>
                    <Switch
                      id="download-unlimited"
                      checked={field.value}
                      onCheckedChange={field.onChange}
                    />
                  </div>
                )}
              />
            </div>
            <Input
              id="download-max-downloads"
              type="number"
              inputMode="numeric"
              min={1}
              max={10_000}
              disabled={downloadUnlimited}
              aria-label="最多下载次数"
              aria-invalid={Boolean(
                form.formState.errors.downloadMaxDownloads,
              )}
              {...form.register("downloadMaxDownloads", { valueAsNumber: true })}
            />
            <FieldDescription>
              达到次数上限后立即失效，最多可设 10000 次。
            </FieldDescription>
            <FieldError
              errors={[form.formState.errors.downloadMaxDownloads]}
            />
          </Field>
        </FieldGroup>
      </FieldSet>

      <Button
        className="w-fit"
        type="submit"
        disabled={mutation.isPending || !form.formState.isDirty}
      >
        {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
        保存系统设置
      </Button>
    </form>
  );
}
