import { useQuery } from '@apollo/client/react'
import Accordion from '@mui/material/Accordion'
import AccordionDetails from '@mui/material/AccordionDetails'
import AccordionSummary from '@mui/material/AccordionSummary'
import Box from '@mui/material/Box'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { ChevronDown, CircleAlert, CircleCheck, FolderOpen } from 'lucide-react'
import { useId, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { Loading } from '../components/Loading.tsx'
import { AdminOverviewDocument } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { useFormat } from '../i18n/format.ts'
import { ErrorMessage } from '../messages.tsx'

/** Something an administrator should act on, with where to do it. */
interface Attention {
  text: string
  href: string
  link: string
}

/**
 * The administration's first page: what needs an administrator now (a restart the saved settings wait for, a settings file the last
 * start left out, a database that does not answer), each with where to act on it, and a few facts about the server. Nothing to act on is
 * said in one line. It needs the server: a copy kept on the device would tell of the wrong state.
 */
export function AdminOverviewPage() {
  const { t } = useTranslation()
  const { number } = useFormat()
  usePageTitle(t('admin.title'))
  const attentionId = useId()
  const factsId = useId()
  const foldersId = useId()
  const { data, error, loading } = useQuery(AdminOverviewDocument, { fetchPolicy: 'no-cache' })

  const content = (): ReactNode => {
    if (error) return <ErrorMessage error={error} />
    if (loading || !data) return <Loading />
    const { systemSettings, users, vehicleCount, health } = data
    const attention: Attention[] = [
      ...(health.databaseReachable ? [] : [{ text: t('adminOverview.databaseDown'), href: '/admin/settings', link: t('nav.serverSettings') }]),
      ...(systemSettings.file.ignored ? [{ text: t('adminOverview.fileIgnored'), href: '/admin/settings', link: t('nav.serverSettings') }] : []),
      ...(systemSettings.restartPending ? [{ text: t('adminOverview.restartPending'), href: '/admin/settings', link: t('nav.serverSettings') }] : []),
    ]
    const admins = users.filter((u) => u.isAdmin).length
    const disabled = users.filter((u) => u.isDisabled).length
    return (
      <Stack sx={{ gap: 3 }}>
        <section aria-labelledby={attentionId}>
          <Typography id={attentionId} component="h2" variant="h5" sx={{ mb: 1 }}>
            {t('adminOverview.attention')}
          </Typography>
          {attention.length === 0 ? (
            <Stack direction="row" sx={{ gap: 1, alignItems: 'center' }}>
              <Box component="span" sx={{ color: 'success.softText', display: 'inline-flex' }}>
                <CircleCheck size={18} aria-hidden />
              </Box>
              <Typography>{t('adminOverview.nothing')}</Typography>
            </Stack>
          ) : (
            <Stack component="ul" sx={{ gap: 1, m: 0, p: 0, listStyle: 'none' }}>
              {attention.map((a) => (
                <Stack component="li" key={a.text} direction="row" sx={{ gap: 1, alignItems: 'flex-start' }}>
                  <Box component="span" sx={{ color: 'warning.softText', display: 'inline-flex', mt: 0.25 }}>
                    <CircleAlert size={18} aria-hidden />
                  </Box>
                  <Typography>
                    {a.text}{' '}
                    <Link component={RouterLink} to={a.href}>
                      {t('adminOverview.goTo', { page: a.link })}
                    </Link>
                  </Typography>
                </Stack>
              ))}
            </Stack>
          )}
        </section>
        <section aria-labelledby={factsId}>
          <Typography id={factsId} component="h2" variant="h5" sx={{ mb: 1 }}>
            {t('adminOverview.server')}
          </Typography>
          <DefinitionList
            items={[
              { label: t('adminOverview.version'), value: health.version },
              { label: t('adminOverview.database'), value: health.databaseReachable ? t('adminOverview.databaseUp') : t('adminOverview.databaseDownShort') },
              {
                label: t('adminOverview.users'),
                value: (
                  <Link component={RouterLink} to="/admin/people">
                    {t('adminOverview.userCount', { count: users.length, users: number(users.length), admins: number(admins), disabled: number(disabled) })}
                  </Link>
                ),
              },
              {
                label: t('adminOverview.vehicles'),
                value: (
                  <Link component={RouterLink} to="/admin/vehicles">
                    {t('adminOverview.vehicleCount', { count: vehicleCount, vehicles: number(vehicleCount) })}
                  </Link>
                ),
              },
            ]}
          />
        </section>
        <Accordion disableGutters variant="outlined" slotProps={{ heading: { component: 'h2' } }}>
          <AccordionSummary expandIcon={<ChevronDown size={18} aria-hidden />} id={`${foldersId}-header`} aria-controls={`${foldersId}-panel`}>
            <Stack direction="row" sx={{ gap: 1, alignItems: 'center' }}>
              <FolderOpen size={18} aria-hidden />
              <Stack>
                <Typography component="span" variant="body1" sx={{ fontWeight: 700 }}>
                  {t('adminOverview.foldersTitle')}
                </Typography>{' '}
                <Typography component="span" variant="caption" sx={{ color: 'text.secondary' }}>
                  {t('adminOverview.foldersHint')}
                </Typography>
              </Stack>
            </Stack>
          </AccordionSummary>
          <AccordionDetails>
            <Stack component="ul" sx={{ gap: 2, m: 0, p: 0, listStyle: 'none' }}>
              {systemSettings.folders.map((f) => (
                <Stack component="li" key={f.kind} sx={{ gap: 0.25 }}>
                  <Typography sx={{ fontWeight: 700 }}>{t(`adminOverview.folders.${f.kind}.name`)}</Typography>
                  <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                    {t(`adminOverview.folders.${f.kind}.what`)}
                  </Typography>
                  {f.sameAsPictures ? (
                    <Typography variant="body2">{t('adminOverview.photosWithPictures')}</Typography>
                  ) : (
                    <Typography variant="body2" sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>
                      {f.path}
                      {f.kind === 'SETTINGS_FILE' && !systemSettings.file.exists && ` (${t('adminOverview.notCreatedYet')})`}
                    </Typography>
                  )}
                  <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                    {t(f.sameAsPictures ? 'adminOverview.ownFolderBy' : 'adminOverview.changedBy')}{' '}
                    <Box component="code" sx={{ fontFamily: 'monospace' }}>
                      {f.variable}
                    </Box>
                  </Typography>
                </Stack>
              ))}
            </Stack>
          </AccordionDetails>
        </Accordion>
      </Stack>
    )
  }

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('admin.title')}
      </Typography>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
        {t('adminOverview.description')}
      </Typography>
      {content()}
    </section>
  )
}
