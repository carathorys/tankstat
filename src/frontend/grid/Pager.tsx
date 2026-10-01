import { Flex, IconButton, Select, Text } from '@radix-ui/themes'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { PAGE_SIZES } from './useGridSettings.ts'

export function Pager({
  page,
  pageSize,
  total,
  onPage,
  onPageSize,
}: {
  /** Zero-based. */
  page: number
  pageSize: number
  total: number
  onPage: (page: number) => void
  onPageSize: (size: number) => void
}) {
  const { t, i18n } = useTranslation()
  const number = new Intl.NumberFormat(i18n.language)
  const from = total === 0 ? 0 : page * pageSize + 1
  const to = Math.min(total, (page + 1) * pageSize)
  const lastPage = Math.max(0, Math.ceil(total / pageSize) - 1)

  return (
    <Flex align="center" justify="between" gap="3" wrap="wrap" mt="3">
      <Flex align="center" gap="2">
        <Select.Root value={String(pageSize)} onValueChange={(v) => onPageSize(Number(v))}>
          <Select.Trigger aria-label={t('grid.pageSize')} />
          <Select.Content>
            {PAGE_SIZES.map((s) => (
              <Select.Item key={s} value={String(s)}>
                {s}
              </Select.Item>
            ))}
          </Select.Content>
        </Select.Root>
      </Flex>
      <Flex align="center" gap="2">
        <Text size="2" color="gray" aria-live="polite">
          {t('grid.range', { from: number.format(from), to: number.format(to), total: number.format(total) })}
        </Text>
        <IconButton variant="soft" color="gray" disabled={page === 0} aria-label={t('grid.previous')} onClick={() => onPage(page - 1)}>
          <ChevronLeft size={18} />
        </IconButton>
        <IconButton variant="soft" color="gray" disabled={page >= lastPage} aria-label={t('grid.next')} onClick={() => onPage(page + 1)}>
          <ChevronRight size={18} />
        </IconButton>
      </Flex>
    </Flex>
  )
}
