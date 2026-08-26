import { z } from 'zod'

const username = z.string().trim().min(3, '用户名至少需要 3 个字符').max(64, '用户名不能超过 64 个字符')
const currentPassword = z.string().min(1, '请输入当前密码').max(128, '密码不能超过 128 个字符')
const newPassword = z.string()
  .min(12, '密码至少需要 12 个字符')
  .max(128, '密码不能超过 128 个字符')
  .regex(/[a-z]/, '密码需要包含小写字母')
  .regex(/[A-Z]/, '密码需要包含大写字母')
  .regex(/[0-9]/, '密码需要包含数字')
  .regex(/[^a-zA-Z0-9]/, '密码需要包含符号')

export const setupSchema = z.object({
  code: z.string().trim().min(16, '请输入 Docker 日志中的完整初始化码').max(64),
  username,
  password: newPassword,
})

export const loginSchema = z.object({ username, password: currentPassword })

export const recoverySchema = z.object({
  username,
  code: z.string().trim().min(16, '请输入完整恢复码').max(64),
  newPassword,
})

export const changePasswordSchema = z.object({
  currentPassword,
  newPassword,
})

export type SetupFormValues = z.infer<typeof setupSchema>
export type LoginFormValues = z.infer<typeof loginSchema>
export type RecoveryFormValues = z.infer<typeof recoverySchema>
export type ChangePasswordFormValues = z.infer<typeof changePasswordSchema>
