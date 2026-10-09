import Divider from '@mui/material/Divider'
import ListItemIcon from '@mui/material/ListItemIcon'
import ListSubheader from '@mui/material/ListSubheader'
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

/** One set of choices in a menu: one of them the current one. `label` heads it when the menu holds more than one set. */
export interface RadioMenuGroup<V extends string = string> {
  label?: string
  value: V
  options: readonly RadioMenuOption<V>[]
  /** Hears only a different choice. */
  onChange: (value: V) => void
}

/**
 * An icon button in the top bar that opens a menu of choices (menu item radios, with a check mark): the language, the appearance. One
 * set (`value`, `options`, `onChange`) or several (`groups`). Several sets are told apart by a separator, which ends a set of radios for
 * assistive technology too, under a heading that is only seen; each item's name says its set ("Surfaces: Opaque"). The items stay the
 * menu's own children, since the menu moves the focus among those (and takes no fragments).
 */
export function RadioMenu<V extends string>(
  props: { label: string; icon: ReactNode } & (
    | { value: V; options: readonly RadioMenuOption<V>[]; onChange: (value: V) => void; groups?: never }
    | { groups: readonly RadioMenuGroup[]; value?: never; options?: never; onChange?: never }
  ),
) {
  const { label, icon } = props
  const groups: readonly RadioMenuGroup[] = props.groups ?? [{ value: props.value, options: props.options, onChange: props.onChange as (value: string) => void }]
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
        {groups.flatMap((group, index) => [
          ...(index > 0 ? [<Divider key={`divider-${index}`} />] : []),
          ...(group.label
            ? [
                <ListSubheader key={`heading-${index}`} role="presentation" disableSticky sx={{ lineHeight: 2.5, backgroundColor: 'transparent' }}>
                  {group.label}
                </ListSubheader>,
              ]
            : []),
          ...group.options.map((option) => (
            <MenuItem
              key={`${index}-${option.value}`}
              role="menuitemradio"
              aria-checked={option.value === group.value}
              aria-label={group.label ? `${group.label}: ${option.label}` : undefined}
              selected={option.value === group.value} // the menu opens on it
              lang={option.lang}
              // The check mark says which one it is, in a narrow column, without the selected item's tint.
              sx={(theme) => ({
                '& .MuiListItemIcon-root': { minWidth: 24 },
                '&.Mui-selected': { backgroundColor: 'transparent' },
                '&.Mui-selected:hover, &.Mui-selected.Mui-focusVisible': { backgroundColor: theme.vars.palette.action.hover },
              })}
              onClick={() => {
                close()
                if (option.value !== group.value) group.onChange(option.value)
              }}
            >
              <ListItemIcon>{option.value === group.value && <Check size={16} aria-hidden />}</ListItemIcon>
              {option.label}
            </MenuItem>
          )),
        ])}
      </Menu>
    </>
  )
}
