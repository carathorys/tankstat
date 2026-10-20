import { useApolloClient } from '@apollo/client/react'
import Avatar from '@mui/material/Avatar'
import Typography from '@mui/material/Typography'
import { useTranslation } from 'react-i18next'
import { DefinitionList } from '../components/DefinitionList.tsx'
import { ImagePicker } from '../components/ImagePicker.tsx'
import { usePictureSrc } from '../offline/keptPictures.ts'
import type { AuthMode, SessionQuery } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ChangePasswordForm } from '../PasswordForms.tsx'
import { avatarPath } from '../pictures/upload.ts'
import { OfflineDataPanel } from './account/OfflineDataPanel.tsx'
import { SessionsPanel } from './account/SessionsPanel.tsx'

export function AccountPage({ mode, user }: { mode: AuthMode; user: SessionQuery['session']['user'] }) {
  const { t } = useTranslation()
  const avatar = usePictureSrc(user?.avatarUrl)
  const client = useApolloClient()
  usePageTitle(t('account.title'))

  return (
    <section aria-labelledby="page-title">
      <Typography id="page-title" component="h1" variant="h3" sx={{ mb: 1.5 }}>
        {t('account.title')}
      </Typography>
      {user ? (
        <DefinitionList
          items={[
            { label: t('account.name'), value: user.displayName },
            { label: t('account.email'), value: user.email || t('common.none') },
            { label: t('account.role'), value: user.isAdmin ? t('role.admin') : t('role.user') },
          ]}
        />
      ) : (
        <Typography>{t('account.noAuth')}</Typography>
      )}
      {user && mode !== 'NONE' && (
        <section aria-labelledby="avatar-heading">
          <Typography component="h2" variant="h5" id="avatar-heading" sx={{ my: 2 }}>
            {t('account.avatar')}
          </Typography>
          <ImagePicker
            preview={
              <Avatar src={avatar.src ?? undefined} alt={t('image.avatarFor', { name: user.displayName })} sx={{ width: 96, height: 96, fontSize: 36 }}>
                {user.displayName.slice(0, 1).toUpperCase()}
              </Avatar>
            }
            hasImage={Boolean(user.avatarUrl)}
            path={avatarPath}
            maxEdge={384}
            square
            onChanged={() => client.refetchQueries({ include: ['Session', 'Admin', 'Vehicles', 'Refuelings', 'Trash', 'RefuelingTrash', 'LogAccess'] })}
          />
          <Typography variant="caption" component="p" sx={{ color: 'text.secondary', mt: 1 }}>
            {t('account.avatarHelp')}
          </Typography>
        </section>
      )}
      {user && mode === 'STANDALONE' && <ChangePasswordForm />}
      {user && mode !== 'STANDALONE' && <Typography>{t('account.managed')}</Typography>}
      {user && (mode === 'STANDALONE' || mode === 'OIDC') && <SessionsPanel mode={mode} />}
      {(user || mode === 'NONE') && <OfflineDataPanel />}
    </section>
  )
}
