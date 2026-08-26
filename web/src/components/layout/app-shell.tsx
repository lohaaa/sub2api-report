import { Outlet, useLocation } from 'react-router-dom'
import { Separator } from '@/components/ui/separator'
import { SidebarInset, SidebarProvider, SidebarTrigger } from '@/components/ui/sidebar'
import { AppSidebar } from '@/components/layout/app-sidebar'
import { ThemeMenu } from '@/components/layout/theme-menu'
import { RouteEffects } from '@/app/route-effects'

const pageNames: Record<string, string> = {
  '/': '工作台',
  '/people': '人员与 Key',
  '/reports': '报告记录',
  '/channels': '发送渠道',
  '/schedule': '计划任务',
  '/settings': '系统设置',
  '/audit': '审计日志',
}

export function AppShell() {
  const location = useLocation()
  const pageName = pageNames[location.pathname] ?? 'Sub2API Report'

  return (
    <SidebarProvider>
      <RouteEffects />
      <AppSidebar />
      <SidebarInset className="min-w-0">
        <header className="sticky top-0 z-10 flex h-14 shrink-0 items-center gap-2 border-b bg-background/95 px-4 backdrop-blur supports-[backdrop-filter]:bg-background/80">
          <SidebarTrigger />
          <Separator orientation="vertical" className="mx-1 h-4" />
          <span className="min-w-0 flex-1 truncate text-sm font-medium">{pageName}</span>
          <ThemeMenu />
        </header>
        <div className="min-w-0 flex-1 p-4 sm:p-6 lg:p-8">
          <Outlet />
        </div>
      </SidebarInset>
    </SidebarProvider>
  )
}
