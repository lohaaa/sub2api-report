import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Controller, useForm } from 'react-hook-form'
import { useEffect } from 'react'
import { z } from 'zod'
import { CheckCircle2Icon } from 'lucide-react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Spinner } from '@/components/ui/spinner'
import { ApiError, getSystemSettings, updateSystemSettings } from '@/lib/api-client'
import { FormError } from '@/features/auth/form-error'

const schema = z.object({
  timezone: z.string().trim().min(1, '请输入 IANA 时区').max(100),
  releaseChannel: z.string().trim().min(1, '请输入发布通道').max(32),
  logLevel: z.enum(['Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal']),
  reportConcurrency: z.number().int().min(1).max(10),
  reportRetentionMonths: z.number().int().min(1).max(120),
  backupRetentionCount: z.number().int().min(1).max(100),
})
type Values = z.infer<typeof schema>

const logLevels = [
  { value: 'Verbose', label: '详细' },
  { value: 'Debug', label: '调试' },
  { value: 'Information', label: '信息' },
  { value: 'Warning', label: '警告' },
  { value: 'Error', label: '错误' },
  { value: 'Fatal', label: '致命' },
] as const

export function SystemSettingsForm() {
  const queryClient = useQueryClient()
  const settingsQuery = useQuery({
    queryKey: ['system-settings'],
    queryFn: ({ signal }) => getSystemSettings(signal),
  })
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      timezone: '',
      releaseChannel: '',
      logLevel: 'Information',
      reportConcurrency: 4,
      reportRetentionMonths: 12,
      backupRetentionCount: 10,
    },
  })
  useEffect(() => {
    if (settingsQuery.data) {
      form.reset({
        timezone: settingsQuery.data.timezone,
        releaseChannel: settingsQuery.data.releaseChannel,
        logLevel: settingsQuery.data.logLevel as Values['logLevel'],
        reportConcurrency: settingsQuery.data.reportConcurrency,
        reportRetentionMonths: settingsQuery.data.reportRetentionMonths,
        backupRetentionCount: settingsQuery.data.backupRetentionCount,
      })
    }
  }, [form, settingsQuery.data])
  const mutation = useMutation({
    mutationFn: (values: Values) => updateSystemSettings({
      ...values,
      revision: settingsQuery.data?.revision ?? 0,
    }),
    onSuccess: async (settings) => {
      queryClient.setQueryData(['system-settings'], settings)
      await queryClient.invalidateQueries({ queryKey: ['system-version'] })
    },
  })

  if (settingsQuery.isPending) {
    return <div className="flex items-center gap-2 text-sm text-muted-foreground"><Spinner />加载设置</div>
  }
  if (settingsQuery.isError) {
    return <FormError message="无法读取系统设置。" />
  }

  return (
    <form className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => mutation.mutate(values))} noValidate>
      <FormError message={mutation.error instanceof ApiError ? mutation.error.message : null} />
      {mutation.isSuccess ? (
        <Alert>
          <CheckCircle2Icon aria-hidden="true" />
          <AlertTitle>设置已保存</AlertTitle>
          <AlertDescription>新配置已写入 revision {mutation.data.revision}。</AlertDescription>
        </Alert>
      ) : null}
      <FieldGroup className="sm:grid sm:grid-cols-2">
        <Field data-invalid={Boolean(form.formState.errors.timezone)}>
          <FieldLabel htmlFor="timezone">默认时区</FieldLabel>
          <Input id="timezone" autoComplete="off" required aria-invalid={Boolean(form.formState.errors.timezone)} {...form.register('timezone')} />
          <FieldError errors={[form.formState.errors.timezone]} />
        </Field>
        <Field data-invalid={Boolean(form.formState.errors.releaseChannel)}>
          <FieldLabel htmlFor="release-channel">发布通道</FieldLabel>
          <Input id="release-channel" autoComplete="off" required aria-invalid={Boolean(form.formState.errors.releaseChannel)} {...form.register('releaseChannel')} />
          <FieldError errors={[form.formState.errors.releaseChannel]} />
        </Field>
        <Controller
          control={form.control}
          name="logLevel"
          render={({ field, fieldState }) => (
            <Field data-invalid={Boolean(fieldState.error)}>
              <FieldLabel htmlFor="log-level">日志级别</FieldLabel>
              <Select items={logLevels} value={field.value} onValueChange={field.onChange}>
                <SelectTrigger id="log-level" className="w-full" aria-invalid={Boolean(fieldState.error)}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {logLevels.map((item) => <SelectItem key={item.value} value={item.value}>{item.label}</SelectItem>)}
                  </SelectGroup>
                </SelectContent>
              </Select>
              <FieldError errors={[fieldState.error]} />
            </Field>
          )}
        />
        <Field data-invalid={Boolean(form.formState.errors.reportConcurrency)}>
          <FieldLabel htmlFor="report-concurrency">报告采集并发数</FieldLabel>
          <Input id="report-concurrency" type="number" inputMode="numeric" min={1} max={10} required aria-invalid={Boolean(form.formState.errors.reportConcurrency)} {...form.register('reportConcurrency', { valueAsNumber: true })} />
          <FieldError errors={[form.formState.errors.reportConcurrency]} />
        </Field>
        <Field data-invalid={Boolean(form.formState.errors.reportRetentionMonths)}>
          <FieldLabel htmlFor="report-retention">报告保留月数</FieldLabel>
          <Input id="report-retention" type="number" inputMode="numeric" min={1} max={120} required aria-invalid={Boolean(form.formState.errors.reportRetentionMonths)} {...form.register('reportRetentionMonths', { valueAsNumber: true })} />
          <FieldError errors={[form.formState.errors.reportRetentionMonths]} />
        </Field>
        <Field data-invalid={Boolean(form.formState.errors.backupRetentionCount)}>
          <FieldLabel htmlFor="backup-retention">备份保留数量</FieldLabel>
          <Input id="backup-retention" type="number" inputMode="numeric" min={1} max={100} required aria-invalid={Boolean(form.formState.errors.backupRetentionCount)} {...form.register('backupRetentionCount', { valueAsNumber: true })} />
          <FieldError errors={[form.formState.errors.backupRetentionCount]} />
        </Field>
      </FieldGroup>
      <Button className="w-fit" type="submit" disabled={mutation.isPending || !form.formState.isDirty}>
        {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
        保存系统设置
      </Button>
    </form>
  )
}
