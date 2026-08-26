import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { ApiError, initializeAdministrator, type SetupStatus } from '@/lib/api-client'
import { AuthLayout } from './auth-layout'
import { FormError } from './form-error'
import { PasswordField } from './password-field'
import { setupSchema, type SetupFormValues } from './schemas'

export function SetupPage({ status }: { status: SetupStatus }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const form = useForm<SetupFormValues>({
    resolver: zodResolver(setupSchema),
    defaultValues: { code: '', username: '', password: '' },
  })
  const mutation = useMutation({
    mutationFn: initializeAdministrator,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['setup-status'] })
      navigate('/login', { replace: true })
    },
  })
  const errorMessage = mutation.error instanceof ApiError ? mutation.error.message : null

  return (
    <AuthLayout
      title="初始化管理员"
      description="输入应用启动日志中的一次性初始化码，然后创建唯一管理员。"
      footer={
        status.challengeExpiresAt
          ? <span className="text-xs text-muted-foreground">初始化码将在 {formatTime(status.challengeExpiresAt)} 过期</span>
          : null
      }
    >
      <form className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => mutation.mutate(values))} noValidate>
        <FormError message={errorMessage} />
        <FieldGroup>
          <Field data-invalid={Boolean(form.formState.errors.code)}>
            <FieldLabel htmlFor="setup-code">初始化码</FieldLabel>
            <Input
              id="setup-code"
              autoComplete="one-time-code"
              enterKeyHint="next"
              spellCheck={false}
              required
              aria-invalid={Boolean(form.formState.errors.code)}
              {...form.register('code')}
            />
            <FieldError errors={[form.formState.errors.code]} />
          </Field>
          <Field data-invalid={Boolean(form.formState.errors.username)}>
            <FieldLabel htmlFor="setup-username">管理员用户名</FieldLabel>
            <Input
              id="setup-username"
              autoComplete="username"
              enterKeyHint="next"
              required
              aria-invalid={Boolean(form.formState.errors.username)}
              {...form.register('username')}
            />
            <FieldError errors={[form.formState.errors.username]} />
          </Field>
          <Field data-invalid={Boolean(form.formState.errors.password)}>
            <FieldLabel htmlFor="new-password">管理员密码</FieldLabel>
            <PasswordField
              id="new-password"
              autoComplete="new-password"
              enterKeyHint="done"
              required
              aria-invalid={Boolean(form.formState.errors.password)}
              {...form.register('password')}
            />
            <FieldDescription>至少 12 个字符，并包含大小写字母、数字和符号。</FieldDescription>
            <FieldError errors={[form.formState.errors.password]} />
          </Field>
        </FieldGroup>
        <Button type="submit" disabled={mutation.isPending}>
          {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
          创建管理员
        </Button>
      </form>
    </AuthLayout>
  )
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(new Date(value))
}
