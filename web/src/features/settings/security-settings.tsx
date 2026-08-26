import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { CheckCircle2Icon } from 'lucide-react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Spinner } from '@/components/ui/spinner'
import { changePassword, createStepUp, ApiError } from '@/lib/api-client'
import { FormError } from '@/features/auth/form-error'
import { PasswordField } from '@/features/auth/password-field'
import { changePasswordSchema, type ChangePasswordFormValues } from '@/features/auth/schemas'

export function SecuritySettings() {
  return (
    <div className="flex flex-col gap-8">
      <ChangePasswordForm />
      <StepUpForm />
    </div>
  )
}

function ChangePasswordForm() {
  const queryClient = useQueryClient()
  const form = useForm<ChangePasswordFormValues>({
    resolver: zodResolver(changePasswordSchema),
    defaultValues: { currentPassword: '', newPassword: '' },
  })
  const mutation = useMutation({
    mutationFn: changePassword,
    onSuccess: async () => {
      form.reset()
      await queryClient.invalidateQueries({ queryKey: ['current-administrator'] })
    },
  })

  return (
    <form className="flex max-w-md flex-col gap-5" onSubmit={form.handleSubmit((values) => mutation.mutate(values))} noValidate>
      <div>
        <h3 className="text-sm font-semibold">修改密码</h3>
        <p className="text-sm text-muted-foreground">保存后当前会话会立即更新。</p>
      </div>
      <FormError message={mutation.error instanceof ApiError ? mutation.error.message : null} />
      {mutation.isSuccess ? <SuccessAlert message="管理员密码已更新。" /> : null}
      <FieldGroup>
        <Field data-invalid={Boolean(form.formState.errors.currentPassword)}>
          <FieldLabel htmlFor="change-current-password">当前密码</FieldLabel>
          <PasswordField id="change-current-password" autoComplete="current-password" required aria-invalid={Boolean(form.formState.errors.currentPassword)} {...form.register('currentPassword')} />
          <FieldError errors={[form.formState.errors.currentPassword]} />
        </Field>
        <Field data-invalid={Boolean(form.formState.errors.newPassword)}>
          <FieldLabel htmlFor="change-new-password">新密码</FieldLabel>
          <PasswordField id="change-new-password" autoComplete="new-password" required aria-invalid={Boolean(form.formState.errors.newPassword)} {...form.register('newPassword')} />
          <FieldDescription>至少 12 个字符，并包含大小写字母、数字和符号。</FieldDescription>
          <FieldError errors={[form.formState.errors.newPassword]} />
        </Field>
      </FieldGroup>
      <Button className="w-fit" type="submit" disabled={mutation.isPending}>
        {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
        更新密码
      </Button>
    </form>
  )
}

function StepUpForm() {
  const queryClient = useQueryClient()
  const form = useForm<{ password: string }>({ defaultValues: { password: '' } })
  const mutation = useMutation({
    mutationFn: createStepUp,
    onSuccess: (session) => {
      form.reset()
      queryClient.setQueryData(['current-administrator'], session)
    },
  })

  return (
    <form className="flex max-w-md flex-col gap-5" onSubmit={form.handleSubmit((values) => mutation.mutate(values))}>
      <div>
        <h3 className="text-sm font-semibold">敏感操作授权</h3>
        <p className="text-sm text-muted-foreground">确认当前密码后获得 10 分钟高风险操作授权。</p>
      </div>
      <FormError message={mutation.error instanceof ApiError ? mutation.error.message : null} />
      {mutation.data?.stepUpExpiresAt ? <SuccessAlert message={`授权有效至 ${formatTime(mutation.data.stepUpExpiresAt)}。`} /> : null}
      <FieldGroup>
        <Field>
          <FieldLabel htmlFor="step-up-password">当前密码</FieldLabel>
          <PasswordField id="step-up-password" autoComplete="current-password" required {...form.register('password', { required: true })} />
        </Field>
      </FieldGroup>
      <Button className="w-fit" type="submit" disabled={mutation.isPending}>
        {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
        确认密码
      </Button>
    </form>
  )
}

function SuccessAlert({ message }: { message: string }) {
  return (
    <Alert>
      <CheckCircle2Icon aria-hidden="true" />
      <AlertTitle>操作成功</AlertTitle>
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  )
}

function formatTime(value: string) {
  return new Intl.DateTimeFormat('zh-CN', { hour: '2-digit', minute: '2-digit' }).format(new Date(value))
}
