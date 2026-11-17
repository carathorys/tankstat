import { useQuery } from '@apollo/client/react'
import Button from '@mui/material/Button'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { DialogFrame } from '../../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../../dialogs/useDialogState.ts'
import { CanExportDocument, ExportCarsDocument, MyExportDocument } from '../../gql/generated.ts'
import { useToast } from '../../toast/toastContext.ts'
import { ExportForm } from '../import/ExportForm.tsx'

/**
 * *Export this car* on an own car's Details tab (#341, the owner's decision 6): the Export tab's choice for this one car in a dialog. A car
 * only shared with the person has no button (it is not among their own cars), nor has anyone while exporting is off for them. After *Export*
 * the dialog closes and a toast leads to the Export tab, where the progress and the file are.
 */
export function ExportCarButton({ vehicleId }: { vehicleId: string }) {
  const { t } = useTranslation()
  const allowed = useQuery(CanExportDocument).data?.canExport === true
  const cars = useQuery(ExportCarsDocument, { skip: !allowed, fetchPolicy: 'cache-and-network' })
  const car = cars.data?.exportCars.find((c) => c.id === vehicleId)
  const [open, setOpen] = useDialogState()
  const mine = useQuery(MyExportDocument, { skip: !open, fetchPolicy: 'network-only' })
  const { toast } = useToast()
  const navigate = useNavigate()
  if (!car || !cars.data) return null

  return (
    <>
      <DialogTrigger
        open={open}
        onOpen={() => setOpen(true)}
        trigger={
          <Button size="large" variant="soft">
            {t('export.car.button')}
          </Button>
        }
      />
      <DialogFrame open={open} onClose={() => setOpen(false)} title={t('export.car.title', { car: car.name })} description={t('export.car.description')} maxWidth={560}>
        <ExportForm
          cars={[car]}
          single
          minLength={cars.data.exportPassphraseMinLength}
          current={mine.data?.myExport?.kept ?? mine.data?.myExport}
          onStarted={() => {
            setOpen(false)
            toast({ message: t('export.started'), severity: 'info', action: { label: t('export.open'), run: () => navigate('/import?tab=export') } })
          }}
        />
      </DialogFrame>
    </>
  )
}
