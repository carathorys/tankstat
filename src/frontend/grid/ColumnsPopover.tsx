import { Button, Checkbox, Flex, IconButton, Popover, Text } from '@radix-ui/themes'
import type { ParseKeys } from 'i18next'
import { ArrowDown, ArrowUp, Columns3, RotateCcw } from 'lucide-react'
import { useTranslation } from 'react-i18next'

export interface ColumnChoice {
  id: string
  label: ParseKeys
  hideable: boolean
}

/** Show/hide and reorder columns. Up/down buttons (not drag and drop) so it works with touch and keyboard. */
export function ColumnsPopover({
  columns,
  hidden,
  onToggle,
  onMove,
  onReset,
}: {
  /** In current display order. */
  columns: ColumnChoice[]
  hidden: string[]
  onToggle: (id: string, visible: boolean) => void
  onMove: (id: string, offset: -1 | 1) => void
  onReset: () => void
}) {
  const { t } = useTranslation()

  return (
    <Popover.Root>
      <Popover.Trigger>
        <IconButton variant="soft" color="gray" aria-label={t('grid.columns')}>
          <Columns3 size={18} />
        </IconButton>
      </Popover.Trigger>
      <Popover.Content width="320px" maxWidth="92vw">
        <Text as="p" size="2" color="gray" mb="3">
          {t('grid.columnsHint')}
        </Text>
        <Flex direction="column" gap="2">
          {columns.map((c, i) => (
            <Flex key={c.id} align="center" gap="2">
              <Checkbox
                aria-label={t('grid.showColumn', { column: t(c.label) })}
                checked={!hidden.includes(c.id)}
                disabled={!c.hideable}
                onCheckedChange={(checked) => onToggle(c.id, checked === true)}
              />
              <Text size="2" style={{ flex: 1 }}>
                {t(c.label)}
              </Text>
              <IconButton
                size="1"
                variant="ghost"
                color="gray"
                disabled={i === 0}
                aria-label={t('grid.moveUp', { column: t(c.label) })}
                onClick={() => onMove(c.id, -1)}
              >
                <ArrowUp size={16} />
              </IconButton>
              <IconButton
                size="1"
                variant="ghost"
                color="gray"
                disabled={i === columns.length - 1}
                aria-label={t('grid.moveDown', { column: t(c.label) })}
                onClick={() => onMove(c.id, 1)}
              >
                <ArrowDown size={16} />
              </IconButton>
            </Flex>
          ))}
        </Flex>
        <Button mt="4" size="1" variant="soft" color="gray" onClick={onReset}>
          <RotateCcw size={14} /> {t('grid.reset')}
        </Button>
      </Popover.Content>
    </Popover.Root>
  )
}
