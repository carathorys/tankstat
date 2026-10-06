import ListItemIcon from '@mui/material/ListItemIcon'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import { Check } from 'lucide-react'
import { useId, useState, type ReactNode } from 'react'
import { IconAction } from '../components/IconAction.tsx'

export interface RadioMenuOption<V extends string> {
  value: V
  label: string
  /** The option's own language, when it is a language's name. */
  lang?: string
}

/**
 * An icon button in the top bar that opens a menu of choices, one of them the current one (menu item radios, with a check mark): the
 * language, the colour mode. `onChange` hears only a different choice.
 */
export function RadioMenu<V extends string>({
  label,
  icon,
  value,
  options,
  onChange,
}: {
  label: string
  icon: ReactNode
  value: V
  options: readonly RadioMenuOption<V>[]
  onChange: (value: V) => void
}) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const menuId = useId()
  const close = () => setAnchor(null)
  return (
    <>
      <IconAction
        label={label}
        aria-haspopup="menu"
        aria-expanded={anchor !== null}
        aria-controls={anchor ? menuId : undefined}
        onClick={(e) => setAnchor(e.currentTarget)}
      >
        {icon}
      </IconAction>
      <Menu
        anchorEl={anchor}
        open={anchor !== null}
        onClose={close}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        slotProps={{ list: { id: menuId, 'aria-label': label } }}
      >
        {options.map((option) => (
          <MenuItem
            key={option.value}
            role="menuitemradio"
            aria-checked={option.value === value}
            selected={option.value === value} // the menu opens on it
            lang={option.lang}
            // The check mark says which one it is, in a narrow column, without the selected item's tint.
            sx={(theme) => ({
              '& .MuiListItemIcon-root': { minWidth: 24 },
              '&.Mui-selected': { backgroundColor: 'transparent' },
              '&.Mui-selected:hover, &.Mui-selected.Mui-focusVisible': { backgroundColor: theme.vars.palette.action.hover },
            })}
            onClick={() => {
              close()
              if (option.value !== value) onChange(option.value)
            }}
          >
            <ListItemIcon>{option.value === value && <Check size={16} aria-hidden />}</ListItemIcon>
            {option.label}
          </MenuItem>
        ))}
      </Menu>
    </>
  )
}
