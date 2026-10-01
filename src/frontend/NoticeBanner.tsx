import { Callout, Flex } from '@radix-ui/themes'
import { Info, TriangleAlert } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { SessionQuery } from './gql/generated.ts'

/** Shows every notice the server sent, translated by its code (the server text is only a fallback). */
export function NoticeBanner({ notices }: { notices: SessionQuery['notices'] }) {
  const { t, i18n } = useTranslation()
  if (notices.length === 0) return null

  return (
    <Flex asChild direction="column" gap="2" mb="4">
      <aside aria-label="Notices">
        {notices.map((n) => {
          const warning = n.severity === 'WARNING'
          const path = `notices.${n.code}`
          return (
            <Callout.Root key={n.code} role="note" data-severity={n.severity} color={warning ? 'amber' : 'blue'}>
              <Callout.Icon>{warning ? <TriangleAlert size={16} /> : <Info size={16} />}</Callout.Icon>
              <Callout.Text>
                {warning && <strong>{t('notices.warning')} </strong>}
                {i18n.exists(path) ? t(path as never) : n.message}
              </Callout.Text>
            </Callout.Root>
          )
        })}
      </aside>
    </Flex>
  )
}
