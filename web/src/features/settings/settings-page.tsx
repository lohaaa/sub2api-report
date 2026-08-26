import { CheckCircle2Icon, DatabaseIcon, RefreshCwIcon, ServerIcon } from 'lucide-react'
import { PageHeader } from '@/components/layout/page-header'
import { Separator } from '@/components/ui/separator'
import { useSystemVersion } from '@/hooks/use-system-version'
import { SecuritySettings } from './security-settings'
import { Sub2ApiConnectionForm } from './sub2api-connection-form'
import { SystemSettingsForm } from './system-settings-form'

export function SettingsPage() {
  const versionQuery = useSystemVersion()

  return (
    <div className="mx-auto flex w-full max-w-4xl flex-col gap-8">
      <PageHeader title="系统设置" description="运行参数、管理员安全和外部连接" />
      <section aria-labelledby="runtime-title" className="flex flex-col gap-4">
        <SectionHeading id="runtime-title" title="运行信息" description="当前应用实例状态" />
        <div className="divide-y rounded-md border">
          <SettingRow icon={ServerIcon} label="应用版本" value={versionQuery.data?.version ?? '未连接'} />
          <SettingRow icon={RefreshCwIcon} label="发布通道" value={versionQuery.data?.releaseChannel ?? 'stable'} />
          <SettingRow icon={DatabaseIcon} label="数据库" value="SQLite" />
        </div>
      </section>
      <Separator />
      <section aria-labelledby="system-settings-title" className="flex flex-col gap-5">
        <SectionHeading id="system-settings-title" title="动态配置" description="设置保存到 SQLite 并在运行期生效" />
        <SystemSettingsForm />
      </section>
      <Separator />
      <section aria-labelledby="security-title" className="flex flex-col gap-5">
        <SectionHeading id="security-title" title="管理员安全" description="密码和敏感操作授权" />
        <SecuritySettings />
      </section>
      <Separator />
      <section aria-labelledby="connection-title" className="flex flex-col gap-5">
        <SectionHeading id="connection-title" title="Sub2API 连接" description="上游地址、机器凭据和 Codex 数据范围" />
        <Sub2ApiConnectionForm />
      </section>
    </div>
  )
}

function SectionHeading({ id, title, description }: { id: string; title: string; description: string }) {
  return (
    <div>
      <h2 id={id} className="text-base font-semibold">{title}</h2>
      <p className="text-sm text-muted-foreground">{description}</p>
    </div>
  )
}

function SettingRow({ icon: Icon, label, value }: { icon: typeof ServerIcon; label: string; value: string }) {
  return (
    <div className="flex min-h-14 items-center gap-3 px-4 py-3">
      <Icon aria-hidden="true" />
      <span className="flex-1 text-sm text-muted-foreground">{label}</span>
      <span className="flex items-center gap-2 text-sm font-medium">
        <CheckCircle2Icon aria-hidden="true" />
        {value}
      </span>
    </div>
  )
}
