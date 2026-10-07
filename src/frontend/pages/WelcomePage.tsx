import { useMutation, useQuery } from '@apollo/client/react'
import Box from '@mui/material/Box'
import Button from '@mui/material/Button'
import InputAdornment from '@mui/material/InputAdornment'
import OutlinedInput from '@mui/material/OutlinedInput'
import Skeleton from '@mui/material/Skeleton'
import Stack from '@mui/material/Stack'
import Typography from '@mui/material/Typography'
import { Search } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { VehicleCard } from '../components/VehicleCard.tsx'
import { itemMotion } from '../components/motion.ts'
import { AddVehicleDocument, WelcomeDocument } from '../gql/generated.ts'
import { useDebouncedValue } from '../hooks/useDebouncedValue.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ArrangeVehiclesDialog } from '../ArrangeVehiclesDialog.tsx'
import { ErrorMessage } from '../messages.tsx'
import { VehicleFormDialog } from '../VehicleFormDialog.tsx'
import { visuallyHidden } from '../components/visuallyHidden.ts'

const PAGE_SIZE = 24

/** The cards: one column on a phone, two from a tablet on, three on a very wide screen. */
const cardGrid = {
  display: 'grid',
  gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'repeat(2, minmax(0, 1fr))', xxl: 'repeat(3, minmax(0, 1fr))' },
  gap: 2,
  listStyle: 'none',
  p: 0,
  m: 0,
} as const

/**
 * The start page: a card for every vehicle the user owns or that is shared with them, with its picture and key figures. A search box
 * (name or license plate, done by the server) narrows them down, and more cards load as the end of the list scrolls into view; a
 * "Show more" button does the same for anyone who does not scroll. This is where everyone adds vehicles; the full list is for
 * administrators (`isAdmin`).
 */
export function WelcomePage({ isAdmin }: { isAdmin: boolean }) {
  const { t } = useTranslation()
  usePageTitle(t('nav.home'))
  const [input, setInput] = useState('')
  const search = useDebouncedValue(input.trim(), 300)
  const { data: current, previousData, error, fetchMore } = useQuery(WelcomeDocument, {
    variables: { search: search || null, skip: 0, take: PAGE_SIZE },
    fetchPolicy: 'cache-and-network',
  })
  const data = current ?? previousData // keep showing the last result while a new search is on its way
  const [addVehicle] = useMutation(AddVehicleDocument, { refetchQueries: ['Welcome'], awaitRefetchQueries: true })
  const [loadingMore, setLoadingMore] = useState(false)
  const [moreError, setMoreError] = useState<unknown>()
  const sentinel = useRef<HTMLDivElement>(null)

  const vehicles = data?.myVehicles ?? []
  const total = data?.myVehicleCount ?? 0
  const hasMore = vehicles.length < total

  const loadMore = useCallback(async () => {
    if (loadingMore || !hasMore) return
    setLoadingMore(true)
    setMoreError(undefined)
    try {
      await fetchMore({
        variables: { skip: vehicles.length },
        updateQuery: (previous, { fetchMoreResult }) =>
          fetchMoreResult ? { ...fetchMoreResult, myVehicles: [...previous.myVehicles, ...fetchMoreResult.myVehicles] } : previous,
      })
    } catch (e) {
      setMoreError(e)
    } finally {
      setLoadingMore(false)
    }
  }, [loadingMore, hasMore, fetchMore, vehicles.length])

  // Load the next page when the end of the list comes near (also again if it is still near after a page arrived).
  useEffect(() => {
    const end = sentinel.current
    if (!end || !hasMore || moreError !== undefined || typeof IntersectionObserver === 'undefined') return
    const observer = new IntersectionObserver((entries) => entries.some((e) => e.isIntersecting) && void loadMore(), { rootMargin: '400px' })
    observer.observe(end)
    return () => observer.disconnect()
  }, [hasMore, moreError, loadMore])

  const addButton = (primary: boolean) => (
    <VehicleFormDialog
      trigger={
        <Button size="large" variant={primary ? 'contained' : 'soft'}>
          {t('welcome.add')}
        </Button>
      }
      onSubmit={(input) => addVehicle({ variables: { input } })}
    />
  )
  const searching = input.trim() !== ''
  const showSearch = searching || total > 0

  return (
    <section aria-labelledby="page-title">
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', gap: 1.5, flexWrap: 'wrap', mb: 2 }}>
        <Typography id="page-title" component="h1" variant="h3">
          {t('welcome.title')}
        </Typography>
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
          <Button component={Link} to="/import" size="large" variant="soft">
            {t('welcome.import')}
          </Button>
          {(data?.vehicleTotal ?? 0) >= 2 && <ArrangeVehiclesDialog />}
          {addButton(!isAdmin)}
          {isAdmin && (
            <Button component={Link} to="/vehicles" size="large">
              {t('welcome.all')}
            </Button>
          )}
        </Stack>
      </Stack>
      {showSearch && (
        <Stack sx={{ gap: 0.5, mb: 2, maxWidth: '28rem' }}>
          <OutlinedInput
            type="search"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            autoComplete="off"
            placeholder={t('welcome.searchPlaceholder')}
            startAdornment={
              <InputAdornment position="start">
                <Search size={16} aria-hidden />
              </InputAdornment>
            }
            inputProps={{ maxLength: 100, 'aria-label': t('welcome.search') }}
            sx={(theme) => ({ ...theme.typography.body1, pl: 1.5, '& .MuiOutlinedInput-input': { height: '1.5rem', py: 1 } })}
          />
          {total > 0 && (
            <Typography variant="body2" role="status" sx={{ color: 'text.secondary' }}>
              {t('welcome.showing', { shown: vehicles.length, total })}
            </Typography>
          )}
        </Stack>
      )}
      {error && <ErrorMessage error={error} />}
      {!data && !error && <CardSkeletons />}
      {data && total === 0 && searching && <Typography>{t('welcome.noMatches', { search: input.trim() })}</Typography>}
      {data && total === 0 && !searching && <Typography>{t('welcome.empty')}</Typography>}
      {vehicles.length > 0 && (
        <Box component="ul" aria-label={t('welcome.countLabel', { count: total })} sx={cardGrid}>
          <AnimatePresence initial={false}>
            {vehicles.map((v) => (
              <motion.li key={v.id} style={{ display: 'grid' }} {...itemMotion}>
                <VehicleCard vehicle={v} />
              </motion.li>
            ))}
          </AnimatePresence>
        </Box>
      )}
      {moreError !== undefined && <ErrorMessage error={moreError} />}
      {hasMore && (
        <Stack ref={sentinel} direction="row" sx={{ justifyContent: 'center', mt: 2 }}>
          <Button size="large" variant="soft" loading={loadingMore} onClick={() => void loadMore()}>
            {t('welcome.more')}
          </Button>
        </Stack>
      )}
    </section>
  )
}

/** While the first cards load: their outlines (only once the wait is noticeable), and "Loading…" for a screen reader. */
function CardSkeletons() {
  const { t } = useTranslation()
  return (
    <>
      <span role="status" style={visuallyHidden}>
        {t('app.loading')}
      </span>
      <Box aria-hidden className="tk-delayed" sx={cardGrid}>
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} variant="rounded" height="17rem" sx={{ borderRadius: '12px' }} />
        ))}
      </Box>
    </>
  )
}
