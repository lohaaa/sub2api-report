import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { ApiError, login } from '@/lib/api-client'
import { AuthLayout } from './auth-layout'
import { FormError } from './form-error'
import { PasswordField } from './password-field'
import { loginSchema, type LoginFormValues } from './schemas'

export function LoginPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { username: '', password: '' },
  })
  const mutation = useMutation({
    mutationFn: login,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['current-administrator'] })
      navigate('/', { replace: true })
    },
  })
  const errorMessage = mutation.error instanceof ApiError ? mutation.error.message : null

  return (
    <AuthLayout
      title="管理员登录"
      description="使用此实例的管理员账户继续。"
      footer={
        <Button variant="link" nativeButton={false} render={<Link to="/recover" />}>
          使用主机恢复码
        </Button>
      }
    >
      <form className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => mutation.mutate(values))} noValidate>
        <FormError message={errorMessage} />
        <FieldGroup>
          <Field data-invalid={Boolean(form.formState.errors.username)}>
            <FieldLabel htmlFor="login-username">用户名</FieldLabel>
            <Input
              id="login-username"
              autoComplete="username"
              enterKeyHint="next"
              required
              aria-invalid={Boolean(form.formState.errors.username)}
              {...form.register('username')}
            />
            <FieldError errors={[form.formState.errors.username]} />
          </Field>
          <Field data-invalid={Boolean(form.formState.errors.password)}>
            <FieldLabel htmlFor="current-password">密码</FieldLabel>
            <PasswordField
              id="current-password"
              autoComplete="current-password"
              enterKeyHint="done"
              required
              aria-invalid={Boolean(form.formState.errors.password)}
              {...form.register('password')}
            />
            <FieldError errors={[form.formState.errors.password]} />
          </Field>
        </FieldGroup>
        <Button type="submit" disabled={mutation.isPending}>
          {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
          登录
        </Button>
      </form>
    </AuthLayout>
  )
}
