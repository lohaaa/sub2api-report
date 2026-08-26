import { Component, type ErrorInfo, type ReactNode } from 'react'
import { AlertCircleIcon, RotateCwIcon } from 'lucide-react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'

export class AppErrorBoundary extends Component<
  { children: ReactNode },
  { hasError: boolean }
> {
  state = { hasError: false }

  static getDerivedStateFromError() {
    return { hasError: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Application render failed', error, info.componentStack)
  }

  render() {
    if (!this.state.hasError) return this.props.children

    return (
      <main className="grid min-h-svh place-items-center p-6">
        <Alert className="max-w-md" variant="destructive">
          <AlertCircleIcon />
          <AlertTitle>页面加载失败</AlertTitle>
          <AlertDescription className="flex flex-col gap-4">
            <span>当前页面无法继续渲染，请重新加载后重试。</span>
            <Button variant="outline" onClick={() => window.location.reload()}>
              <RotateCwIcon data-icon="inline-start" />
              重新加载
            </Button>
          </AlertDescription>
        </Alert>
      </main>
    )
  }
}
