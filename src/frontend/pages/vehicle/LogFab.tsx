import Fab from '@mui/material/Fab'
import ListItemIcon from '@mui/material/ListItemIcon'
import Menu from '@mui/material/Menu'
import MenuItem from '@mui/material/MenuItem'
import { Fuel, Plus, Receipt } from 'lucide-react'
import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ExpenseFormDialog } from '../../ExpenseFormDialog.tsx'
import type { DistanceUnit, VolumeUnit } from '../../gql/generated.ts'
import { RefuelingFormDialog } from '../../RefuelingFormDialog.tsx'
import { useLogMutations } from './useLogMutations.ts'

/**
 * On a phone, the vehicle page's add button floats at the bottom corner, within reach of the thumb on every tab: it opens a menu (a
 * refuelling or an expense) and then that dialog. Only for someone who may add logs; clear of the home indicator of an installed app.
 */
export function LogFab({ vehicle }: { vehicle: { id: string; units: { distance: DistanceUnit; volume: VolumeUnit } } }) {
  const { t } = useTranslation()
  const add = useLogMutations(vehicle.id)
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  // The dialog opens once the menu is gone and has handed the focus back to the button, so the dialog hands it back there too.
  const [chosen, setChosen] = useState<'refuel' | 'expense' | null>(null)
  const [dialog, setDialog] = useState<'refuel' | 'expense' | null>(null)
  const menuId = useId()
  const choose = (which: 'refuel' | 'expense') => {
    setChosen(which)
    setAnchor(null)
  }
  return (
    <>
      <Fab
        color="primary"
        aria-label={t('vehicles.addLog')}
        aria-haspopup="menu"
        aria-expanded={anchor !== null}
        aria-controls={anchor ? menuId : undefined}
        onClick={(e) => setAnchor(e.currentTarget)}
        sx={(theme) => ({
          position: 'fixed',
          right: 'calc(16px + env(safe-area-inset-right, 0px))',
          bottom: 'calc(16px + env(safe-area-inset-bottom, 0px))',
          zIndex: theme.zIndex.fab,
        })}
      >
        <Plus size={24} aria-hidden />
      </Fab>
      <Menu
        anchorEl={anchor}
        open={anchor !== null}
        onClose={() => setAnchor(null)}
        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
        transformOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        slotProps={{
          list: { id: menuId, 'aria-label': t('vehicles.addLog') },
          transition: {
            onExited: () => {
              setDialog(chosen)
              setChosen(null)
            },
          },
        }}
      >
        <MenuItem onClick={() => choose('refuel')}>
          <ListItemIcon>
            <Fuel size={16} aria-hidden />
          </ListItemIcon>
          {t('refuelings.add')}
        </MenuItem>
        <MenuItem onClick={() => choose('expense')}>
          <ListItemIcon>
            <Receipt size={16} aria-hidden />
          </ListItemIcon>
          {t('expenses.add')}
        </MenuItem>
      </Menu>
      <RefuelingFormDialog vehicle={vehicle} open={dialog === 'refuel'} onOpenChange={(next) => !next && setDialog(null)} onSubmit={add.refuel} />
      <ExpenseFormDialog vehicle={vehicle} open={dialog === 'expense'} onOpenChange={(next) => !next && setDialog(null)} onSubmit={add.expense} />
    </>
  )
}
