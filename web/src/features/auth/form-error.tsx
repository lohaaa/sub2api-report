import { CircleAlertIcon } from 'lucide-react'
import { useEffect, useRef } from 'react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'

export function FormError({ message }: { message: string | null }) {
  const alertRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (message) {
      alertRef.current?.focus()
    }
  }, [message])

  if (!message) {
    return null
  }

  return (
    <Alert ref={alertRef} variant="destructive" role="alert" tabIndex={-1}>
      <CircleAlertIcon aria-hidden="true" />
      <AlertTitle>操作失败</AlertTitle>
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  )
}
