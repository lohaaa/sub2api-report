import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2Icon, PlugZapIcon, ShieldAlertIcon } from 'lucide-react'
import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { FormError } from '@/features/auth/form-error'
import { PasswordField } from '@/features/auth/password-field'
import {
  ApiError,
  getSub2ApiConnection,
  saveSub2ApiConnection,
  testSub2ApiConnection,
} from '@/lib/api-client'

const positiveId = /^[1-9][0-9]{0,18}$/
const schema = z.object({
  baseUrl: z.url('请输入完整的 HTTP 或 HTTPS 地址').max(2048).regex(/^https?:\/\//i, '只允许 HTTP 或 HTTPS 地址'),
  adminApiKey: z.string().max(4096),
  userId: z.string().regex(positiveId, '请输入正整数用户 ID'),
  codexGroupId: z.string().refine((value) => value === '' || positiveId.test(value), '请输入正整数分组 ID'),
})
type Values = z.infer<typeof schema>

export function Sub2ApiConnectionForm() {
  const queryClient = useQueryClient()
  const connectionQuery = useQuery({
    queryKey: ['sub2api-connection'],
    queryFn: ({ signal }) => getSub2ApiConnection(signal),
  })
  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { baseUrl: '', adminApiKey: '', userId: '', codexGroupId: '' },
  })
  useEffect(() => {
    if (connectionQuery.data) {
      form.reset({
        baseUrl: connectionQuery.data.baseUrl ?? '',
        adminApiKey: '',
        userId: connectionQuery.data.userId ?? '',
        codexGroupId: connectionQuery.data.codexGroupId ?? '',
      })
    }
  }, [connectionQuery.data, form])
  const saveMutation = useMutation({
    mutationFn: (values: Values) => saveSub2ApiConnection({
      baseUrl: values.baseUrl,
      adminApiKey: values.adminApiKey || null,
      clearAdminApiKey: false,
      userId: values.userId,
      codexGroupId: values.codexGroupId || null,
      revision: connectionQuery.data?.revision ?? 0,
    }),
    onSuccess: (connection) => {
      queryClient.setQueryData(['sub2api-connection'], connection)
      form.reset({
        baseUrl: connection.baseUrl ?? '',
        adminApiKey: '',
        userId: connection.userId ?? '',
        codexGroupId: connection.codexGroupId ?? '',
      })
    },
  })
  const testMutation = useMutation({
    mutationFn: testSub2ApiConnection,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['sub2api-connection'] })
    },
  })

  if (connectionQuery.isPending) {
    return <div className="flex items-center gap-2 text-sm text-muted-foreground"><Spinner />加载连接配置</div>
  }
  if (connectionQuery.isError) {
    return <FormError message="无法读取 Sub2API 连接配置。" />
  }

  const connection = connectionQuery.data
  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant={connection.configured ? 'secondary' : 'outline'}>
          {connection.configured ? '已配置' : '未配置'}
        </Badge>
        {connection.adminApiKeyMask ? <Badge variant="outline">密钥 {connection.adminApiKeyMask}</Badge> : null}
        {connection.lastTestSucceeded !== null ? (
          <Badge variant={connection.lastTestSucceeded ? 'secondary' : 'outline'}>
            {connection.lastTestSucceeded ? '连接测试通过' : `连接测试失败 · ${connection.lastTestCode ?? 'unknown'}`}
          </Badge>
        ) : null}
        {connection.lastSynchronizedAt ? (
          <span className="text-xs text-muted-foreground">
            最近同步 {formatDateTime(connection.lastSynchronizedAt)} · {connection.lastSynchronizedKeyCount ?? 0} 个 Key
          </span>
        ) : null}
      </div>
      <form
        className="flex flex-col gap-5"
        onSubmit={form.handleSubmit((values) => {
          if (!connection.hasAdminApiKey && !values.adminApiKey) {
            form.setError('adminApiKey', { message: '首次保存必须填写 Admin API Key' })
            return
          }
          saveMutation.mutate(values)
        })}
        noValidate
      >
        <FormError message={saveMutation.error instanceof ApiError ? saveMutation.error.message : null} />
        {saveMutation.error instanceof ApiError && saveMutation.error.status === 403 ? (
          <Alert variant="destructive">
            <ShieldAlertIcon aria-hidden="true" />
            <AlertTitle>需要敏感操作授权</AlertTitle>
            <AlertDescription>请先在管理员安全中确认当前密码，再保存连接配置。</AlertDescription>
          </Alert>
        ) : null}
        {saveMutation.isSuccess ? (
          <Alert>
            <CheckCircle2Icon aria-hidden="true" />
            <AlertTitle>连接配置已保存</AlertTitle>
            <AlertDescription>当前 revision 为 {saveMutation.data.revision}。</AlertDescription>
          </Alert>
        ) : null}
        <FieldGroup className="sm:grid sm:grid-cols-2">
          <Field className="sm:col-span-2" data-invalid={Boolean(form.formState.errors.baseUrl)}>
            <FieldLabel htmlFor="sub2api-base-url">Base URL</FieldLabel>
            <Input
              id="sub2api-base-url"
              type="url"
              inputMode="url"
              autoComplete="url"
              required
              aria-invalid={Boolean(form.formState.errors.baseUrl)}
              {...form.register('baseUrl')}
            />
            <FieldError errors={[form.formState.errors.baseUrl]} />
          </Field>
          <Field className="sm:col-span-2" data-invalid={Boolean(form.formState.errors.adminApiKey)}>
            <FieldLabel htmlFor="sub2api-admin-key">Admin API Key</FieldLabel>
            <PasswordField
              id="sub2api-admin-key"
              autoComplete="off"
              aria-invalid={Boolean(form.formState.errors.adminApiKey)}
              {...form.register('adminApiKey')}
            />
            <FieldDescription>
              {connection.hasAdminApiKey ? '留空会保留当前密钥；输入新值会替换密钥。' : '首次保存必须填写密钥。'}
            </FieldDescription>
            <FieldError errors={[form.formState.errors.adminApiKey]} />
          </Field>
          <Field data-invalid={Boolean(form.formState.errors.userId)}>
            <FieldLabel htmlFor="sub2api-user-id">用户 ID</FieldLabel>
            <Input
              id="sub2api-user-id"
              inputMode="numeric"
              autoComplete="off"
              required
              aria-invalid={Boolean(form.formState.errors.userId)}
              {...form.register('userId')}
            />
            <FieldError errors={[form.formState.errors.userId]} />
          </Field>
          <Field data-invalid={Boolean(form.formState.errors.codexGroupId)}>
            <FieldLabel htmlFor="sub2api-group-id">Codex Group ID</FieldLabel>
            <Input
              id="sub2api-group-id"
              inputMode="numeric"
              autoComplete="off"
              aria-invalid={Boolean(form.formState.errors.codexGroupId)}
              {...form.register('codexGroupId')}
            />
            <FieldDescription>留空时同步目标用户的全部 Key。</FieldDescription>
            <FieldError errors={[form.formState.errors.codexGroupId]} />
          </Field>
        </FieldGroup>
        <div className="flex flex-wrap gap-2">
          <Button type="submit" disabled={saveMutation.isPending || !form.formState.isDirty}>
            {saveMutation.isPending ? <Spinner data-icon="inline-start" /> : null}
            保存连接配置
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={!connection.configured || testMutation.isPending}
            onClick={() => testMutation.mutate()}
          >
            {testMutation.isPending ? <Spinner data-icon="inline-start" /> : <PlugZapIcon data-icon="inline-start" />}
            测试连接
          </Button>
        </div>
      </form>
      {testMutation.data ? (
        <Alert variant={testMutation.data.succeeded ? 'default' : 'destructive'}>
          {testMutation.data.succeeded ? <CheckCircle2Icon aria-hidden="true" /> : <ShieldAlertIcon aria-hidden="true" />}
          <AlertTitle>{testMutation.data.succeeded ? '连接成功' : '连接失败'}</AlertTitle>
          <AlertDescription>
            {testMutation.data.message}
            {testMutation.data.availableKeyCount === null ? '' : ` 上游当前有 ${testMutation.data.availableKeyCount} 个 Key。`}
          </AlertDescription>
        </Alert>
      ) : null}
      <FormError message={testMutation.error instanceof ApiError ? testMutation.error.message : null} />
    </div>
  )
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}
