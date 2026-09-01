import {
  CalendarClockIcon,
  ChartNoAxesCombinedIcon,
  FileChartColumnIcon,
  KeyRoundIcon,
  LogOutIcon,
  MegaphoneIcon,
  SettingsIcon,
  UsersIcon,
} from 'lucide-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { NavLink, useLocation, useNavigate } from 'react-router-dom'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarRail,
} from '@/components/ui/sidebar'
import { Badge } from '@/components/ui/badge'
import { useSystemVersion } from '@/hooks/use-system-version'
import { logout } from '@/lib/api-client'

const navigation = [
  { title: '工作台', to: '/', icon: ChartNoAxesCombinedIcon },
  { title: 'API Keys', to: '/keys', icon: UsersIcon },
  { title: '报告记录', to: '/reports', icon: FileChartColumnIcon },
  { title: '发送渠道', to: '/channels', icon: MegaphoneIcon },
  { title: '计划任务', to: '/schedule', icon: CalendarClockIcon },
  { title: '系统设置', to: '/settings', icon: SettingsIcon },
] as const

export function AppSidebar() {
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const versionQuery = useSystemVersion()
  const logoutMutation = useMutation({
    mutationFn: logout,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['current-administrator'] })
      navigate('/login', { replace: true })
    },
  })

  return (
    <Sidebar collapsible="icon" role="navigation" aria-label="主导航">
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" render={<NavLink to="/" />} tooltip="Sub2API Report">
              <span className="grid size-8 shrink-0 place-items-center rounded-md bg-primary text-primary-foreground">
                <KeyRoundIcon aria-hidden="true" />
              </span>
              <span className="flex min-w-0 flex-col text-left">
                <span className="truncate font-semibold">Sub2API Report</span>
                <span className="truncate text-xs text-muted-foreground">Codex 用量报告</span>
              </span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>管理</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              {navigation.map((item) => {
                const isActive = item.to === '/'
                  ? location.pathname === '/'
                  : location.pathname.startsWith(item.to)
                return (
                  <SidebarMenuItem key={item.to}>
                    <SidebarMenuButton
                      render={<NavLink to={item.to} />}
                      isActive={isActive}
                      tooltip={item.title}
                    >
                      <item.icon />
                      <span>{item.title}</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                )
              })}
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
      <SidebarFooter>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton
              tooltip="退出登录"
              onClick={() => logoutMutation.mutate()}
              disabled={logoutMutation.isPending}
            >
              <LogOutIcon />
              <span>退出登录</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
          <SidebarMenuItem>
            <SidebarMenuButton tooltip={versionQuery.isSuccess ? `版本 ${versionQuery.data.version}` : '服务状态'}>
              <span className="relative flex size-4 items-center justify-center" aria-hidden="true">
                <span className={`size-2 rounded-full ${versionQuery.isSuccess ? 'bg-chart-2' : 'bg-muted-foreground'}`} />
              </span>
              <span className="truncate text-xs text-muted-foreground">
                {versionQuery.isSuccess ? `v${versionQuery.data.version}` : '服务未连接'}
              </span>
              <Badge variant="outline" className="ml-auto group-data-[collapsible=icon]:hidden">
                {versionQuery.isSuccess ? '正常' : '离线'}
              </Badge>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  )
}
