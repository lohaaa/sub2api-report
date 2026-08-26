import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'
import { CheckCircle2Icon } from 'lucide-react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { ApiError, recoverAdministrator } from '@/lib/api-client'
import { AuthLayout } from './auth-layout'
import { FormError } from './form-error'
import { PasswordField } from './password-field'
import { recoverySchema, type RecoveryFormValues } from './schemas'

export function RecoveryPage() {
  const [completed, setCompleted] = useState(false)
  const form = useForm<RecoveryFormValues>({
    resolver: zodResolver(recoverySchema),
    defaultValues: { username: '', code: '', newPassword: '' },
  })
  const mutation = useMutation({
    mutationFn: recoverAdministrator,
    onSuccess: () => setCompleted(true),
  })
  const errorMessage = mutation.error instanceof ApiError ? mutation.error.message : null

  return (
    <AuthLayout
      title="恢复管理员访问"
      description="输入主机命令生成的一次性恢复码并设置新密码。"
      footer={
        <Button variant="link" nativeButton={false} render={<Link to="/login" />}>
          返回登录
        </Button>
      }
    >
      {completed ? (
        <Alert>
          <CheckCircle2Icon aria-hidden="true" />
          <AlertTitle>密码已更新</AlertTitle>
          <AlertDescription>恢复码已经失效，可以使用新密码登录。</AlertDescription>
        </Alert>
      ) : (
        <form className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => mutation.mutate(values))} noValidate>
          <FormError message={errorMessage} />
          <FieldGroup>
            <Field data-invalid={Boolean(form.formState.errors.username)}>
              <FieldLabel htmlFor="recovery-username">用户名</FieldLabel>
              <Input
                id="recovery-username"
                autoComplete="username"
                enterKeyHint="next"
                required
                aria-invalid={Boolean(form.formState.errors.username)}
                {...form.register('username')}
              />
              <FieldError errors={[form.formState.errors.username]} />
            </Field>
            <Field data-invalid={Boolean(form.formState.errors.code)}>
              <FieldLabel htmlFor="recovery-code">恢复码</FieldLabel>
              <Input
                id="recovery-code"
                autoComplete="one-time-code"
                enterKeyHint="next"
                spellCheck={false}
                required
                aria-invalid={Boolean(form.formState.errors.code)}
                {...form.register('code')}
              />
              <FieldError errors={[form.formState.errors.code]} />
            </Field>
            <Field data-invalid={Boolean(form.formState.errors.newPassword)}>
              <FieldLabel htmlFor="recovery-new-password">新密码</FieldLabel>
              <PasswordField
                id="recovery-new-password"
                autoComplete="new-password"
                enterKeyHint="done"
                required
                aria-invalid={Boolean(form.formState.errors.newPassword)}
                {...form.register('newPassword')}
              />
              <FieldDescription>至少 12 个字符，并包含大小写字母、数字和符号。</FieldDescription>
              <FieldError errors={[form.formState.errors.newPassword]} />
            </Field>
          </FieldGroup>
          <Button type="submit" disabled={mutation.isPending}>
            {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
            重置密码
          </Button>
        </form>
      )}
    </AuthLayout>
  )
}
