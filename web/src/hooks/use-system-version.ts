import { useQuery } from '@tanstack/react-query'
import { getSystemVersion } from '@/lib/api-client'

export function useSystemVersion() {
  return useQuery({
    queryKey: ['system', 'version'],
    queryFn: ({ signal }) => getSystemVersion(signal),
  })
}
