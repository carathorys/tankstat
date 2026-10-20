import { useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Stack from '@mui/material/Stack'
import TableBody from '@mui/material/TableBody'
import TableCell from '@mui/material/TableCell'
import TableHead from '@mui/material/TableHead'
import TableRow from '@mui/material/TableRow'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { Loading } from '../components/Loading.tsx'
import { SurfaceTable } from '../components/SurfaceTable.tsx'
import { DialogButtons, DialogCancel, DialogFrame } from '../dialogs/DialogFrame.tsx'
import { DialogTrigger } from '../dialogs/DialogTrigger.tsx'
import { useDialogState } from '../dialogs/useDialogState.ts'
import { BackupDetailsDocument, SetupBackupDetailsDocument, type BackupDetailsFieldsFragment } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage } from '../messages.tsx'
import { useHeldText } from './backupMarks.ts'

/**
 * A backup opened: what its plain manifest says, the tables with their rows, the sizes, the checksum, its settings file and what it recorded
 * of its server's environment (#140's improvements). Nothing is decrypted or unpacked, so it opens an encrypted backup without its key. Asked
 * for only once the dialog opens; `code`: the setup page's, while the setup waits (its own module, so that page never loads the Backups page).
 */
export function BackupDetailsDialog({ file, createdAt, code, trigger }: { file: string; createdAt: string; code?: string; trigger: ReactNode }) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const [open, setOpen] = useDialogState()
  return (
    <>
      <DialogTrigger trigger={trigger} open={open} onOpen={() => setOpen(true)} />
      <DialogFrame
        open={open}
        onClose={() => setOpen(false)}
        maxWidth={640}
        title={t('admin.backups.details.title', { made: dateTime(createdAt), file })}
        description={t('admin.backups.details.description')}
      >
        {open && (code === undefined ? <AdminDetails file={file} /> : <SetupDetails file={file} code={code} />)}
        <DialogButtons>
          <DialogCancel label={t('common.close')} />
        </DialogButtons>
      </DialogFrame>
    </>
  )
}

function AdminDetails({ file }: { file: string }) {
  const { data, error } = useQuery(BackupDetailsDocument, { variables: { file }, fetchPolicy: 'network-only' })
  return <DetailsBody details={data?.backupDetails} error={error} />
}

function SetupDetails({ file, code }: { file: string; code: string }) {
  const { data, error } = useQuery(SetupBackupDetailsDocument, { variables: { file, code }, fetchPolicy: 'network-only' })
  return <DetailsBody details={data?.setupBackupDetails} error={error} />
}

function DetailsBody({ details: d, error }: { details: BackupDetailsFieldsFragment | undefined; error: unknown }) {
  const { t } = useTranslation()
  const { dateTime, bytes, number } = useFormat()
  const heldText = useHeldText()
  if (error) return <ErrorMessage error={error} />
  if (!d) return <Loading />
  const environmentMode = (mode: string) => t(mode === 'MERGE' ? 'systemSettings.choices.backupEnvironment.Merge' : 'systemSettings.choices.backupEnvironment.Record')
  const names = (list: readonly string[]) => (list.length > 0 ? list.join(', ') : t('admin.backups.details.noneNamed'))
  const rows = d.tables.reduce((sum, table) => sum + table.rows, 0)
  return (
    <Stack sx={{ gap: 2.5, mb: 1 }}>
      <Section title={t('admin.backups.details.backup')}>
        <DefinitionList
          items={[
            { label: t('admin.backups.details.made'), value: dateTime(d.createdAt) },
            { label: t('admin.backups.details.file'), value: d.file },
            { label: t('admin.backups.details.size'), value: bytes(d.bytes) },
            {
              label: t('admin.backups.details.kind'),
              value: d.safetyNet ? t('admin.backups.safetyNet') : d.uploaded ? t('admin.backups.uploadedTag') : t('admin.backups.kindBackup'),
            },
            { label: t('admin.backups.details.hold'), value: heldText(d) ?? t('admin.backups.details.notHeld') },
            { label: t('admin.backups.details.writtenBy'), value: t('admin.backups.details.writtenByValue', { by: d.writtenBy, version: d.appVersion, format: d.format }) },
            {
              label: t('admin.backups.details.database'),
              value: d.migration ? t('admin.backups.details.databaseAt', { provider: d.provider, migration: d.migration }) : d.provider,
            },
          ]}
        />
      </Section>
      <Section title={t('admin.backups.details.encryption')}>
        <DefinitionList
          items={[
            {
              label: t('admin.backups.details.encryption'),
              value: !d.encrypted ? t('admin.backups.plain') : d.currentRecipient ? t('admin.backups.encryptedCurrent') : t('admin.backups.encryptedOther'),
            },
            ...(d.encrypted && d.recipient ? [{ label: t('admin.backups.details.recipient'), value: <Mono>{d.recipient}</Mono> }] : []),
            ...(d.encryptionMethod ? [{ label: t('admin.backups.details.method'), value: d.encryptionMethod }] : []),
            { label: t('admin.backups.details.keys'), value: d.keys ? t('admin.backups.details.keysIn') : t('admin.backups.details.keysOut') },
          ]}
        />
      </Section>
      <Section title={t('admin.backups.details.contents')}>
        <DefinitionList
          items={[
            { label: t('admin.backups.details.pictures'), value: d.pictures ? t('admin.backups.details.included') : t('admin.backups.details.notIncluded') },
            { label: t('admin.backups.details.photos'), value: d.photos === 'LEFT_OUT' ? t('admin.backups.withoutPhotos') : t('admin.backups.withPhotos') },
          ]}
        />
        <Box sx={{ mt: 1.5 }}>
          <SurfaceTable caption={t('admin.backups.details.tablesCaption', { tables: number(d.tables.length), rows: number(rows) })}>
            <TableHead>
              <TableRow>
                <TableCell>{t('admin.backups.details.table')}</TableCell>
                <TableCell align="right">{t('admin.backups.details.rows')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {d.tables.map((table) => (
                <TableRow key={table.name}>
                  <TableCell component="th" scope="row" sx={{ overflowWrap: 'anywhere' }}>
                    {table.name}
                  </TableCell>
                  <TableCell align="right">{number(table.rows)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </SurfaceTable>
        </Box>
      </Section>
      <Section title={t('admin.backups.details.sizes')}>
        <DefinitionList
          items={[
            { label: t('admin.backups.details.sizeTables'), value: bytes(d.sizes.tables) },
            { label: t('admin.backups.details.sizePictures'), value: bytes(d.sizes.pictures) },
            { label: t('admin.backups.details.sizePhotos'), value: bytes(d.sizes.photos) },
            { label: t('admin.backups.details.sizeKeys'), value: bytes(d.sizes.keys) },
            { label: t('admin.backups.details.sizeArchive', { entry: d.archiveEntry }), value: bytes(d.sizes.archive) },
            { label: t('admin.backups.details.checksum'), value: <Mono>{d.sha256}</Mono> },
          ]}
        />
      </Section>
      <Section title={t('admin.backups.details.settingsFile')}>
        {d.settingsFile ? (
          <DefinitionList
            items={[
              {
                label: t('admin.backups.details.settingsState'),
                value:
                  d.settingsFile.state === 'INCLUDED'
                    ? t('admin.backups.details.settingsIncluded', { size: bytes(d.settingsFile.bytes) })
                    : d.settingsFile.state === 'IGNORED'
                      ? t('admin.backups.details.settingsIgnored')
                      : t('admin.backups.details.settingsNone'),
              },
              ...(d.settingsFile.state === 'INCLUDED'
                ? [
                    {
                      label: t('admin.backups.details.secrets'),
                      value: !d.settingsFile.secrets ? t('admin.backups.secretsLeftOut') : d.encrypted ? t('admin.backups.secretsIncluded') : t('admin.backups.secretsIncludedPlain'),
                    },
                    { label: t('admin.backups.details.secretsLeftOut'), value: names(d.settingsFile.secretsLeftOut) },
                    { label: t('admin.backups.details.machineLeftOut'), value: names(d.settingsFile.machineLeftOut) },
                  ]
                : []),
            ]}
          />
        ) : (
          <Typography variant="body2">{t('admin.backups.details.settingsOlder')}</Typography>
        )}
      </Section>
      <Section title={t('admin.backups.details.environment')}>
        {d.environment ? (
          <DefinitionList
            items={[
              { label: t('admin.backups.details.environmentMode'), value: environmentMode(d.environment.mode) },
              { label: t('admin.backups.details.environmentSettings'), value: names(d.environment.settings) },
              { label: t('admin.backups.details.environmentServer'), value: names(d.environment.server) },
            ]}
          />
        ) : (
          <Typography variant="body2">{t('admin.backups.details.environmentOlder')}</Typography>
        )}
      </Section>
    </Stack>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Box component="section" aria-label={title}>
      <Typography component="h3" variant="subtitle2" sx={{ mb: 1 }}>
        {title}
      </Typography>
      {children}
    </Box>
  )
}

/** A value read character by character (a key, a checksum): in a fixed-width font, broken anywhere. */
function Mono({ children }: { children: ReactNode }) {
  return (
    <Box component="span" sx={{ fontFamily: 'monospace', overflowWrap: 'anywhere' }}>
      {children}
    </Box>
  )
}
