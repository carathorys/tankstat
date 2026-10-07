import Box from '@mui/material/Box'
import Chip from '@mui/material/Chip'
import Link from '@mui/material/Link'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link as RouterLink } from 'react-router'
import type { NotificationFieldsFragment } from '../gql/generated.ts'
import { useFormat } from '../i18n/format.ts'
import { useNotificationActions } from './useNotificationActions.ts'
import { useNotificationText } from './useNotificationText.ts'

/**
 * One notification: what happened (a link when there is something to look at; following it marks it read), when, and whether it is new
 * (as a word, not only bold). `onFollow` lets the bell's popover close itself when the link is followed.
 */
export function NotificationItem({ notification, onFollow, actions }: { notification: NotificationFieldsFragment; onFollow?: () => void; actions?: ReactNode }) {
  const { t } = useTranslation()
  const { dateTime } = useFormat()
  const { open } = useNotificationActions()
  const { text, href } = useNotificationText()(notification)

  return (
    <Stack direction="row" sx={{ gap: 1.5, alignItems: 'flex-start', py: 1 }}>
      <Box sx={{ flexGrow: 1, minWidth: 0 }}>
        <Typography variant="body2" sx={{ fontWeight: notification.read ? 'fontWeightRegular' : 'fontWeightBold' }}>
          {!notification.read && <Chip component="span" color="info" label={t('notifications.new')} sx={{ mr: 1 }} />}
          {href ? (
            <Link
              component={RouterLink}
              to={href}
              onClick={() => {
                open(notification)
                onFollow?.()
              }}
            >
              {text}
            </Link>
          ) : (
            text
          )}
        </Typography>
        <Typography variant="caption" component="p" sx={{ color: 'text.secondary' }}>
          <time dateTime={notification.updatedAt}>{dateTime(notification.updatedAt)}</time>
        </Typography>
      </Box>
      {actions}
    </Stack>
  )
}
