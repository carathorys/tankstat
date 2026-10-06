import { useMutation, useQuery } from '@apollo/client/react'
import { Button, Flex, Grid, Heading, Text, TextField } from '@radix-ui/themes'
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
import { Loading } from '../components/Loading.tsx'

const PAGE_SIZE = 24

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
      trigger={<Button size="3" variant={primary ? 'solid' : 'soft'}>{t('welcome.add')}</Button>}
      onSubmit={(input) => addVehicle({ variables: { input } })}
    />
  )
  const searching = input.trim() !== ''
  const showSearch = searching || total > 0

  return (
    <section aria-labelledby="page-title">
      <Flex justify="between" align="center" gap="3" wrap="wrap" mb="4">
        <Heading id="page-title">{t('welcome.title')}</Heading>
        <Flex gap="2" wrap="wrap">
          <Button asChild size="3" variant="soft">
            <Link to="/import">{t('welcome.import')}</Link>
          </Button>
          {(data?.vehicleTotal ?? 0) >= 2 && <ArrangeVehiclesDialog />}
          {addButton(!isAdmin)}
          {isAdmin && (
            <Button asChild size="3">
              <Link to="/vehicles">{t('welcome.all')}</Link>
            </Button>
          )}
        </Flex>
      </Flex>
      {showSearch && (
        <Flex direction="column" gap="1" mb="4" style={{ maxWidth: '28rem' }}>
          <TextField.Root
            type="search"
            size="3"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            maxLength={100}
            autoComplete="off"
            aria-label={t('welcome.search')}
            placeholder={t('welcome.searchPlaceholder')}
          >
            <TextField.Slot>
              <Search size={16} aria-hidden />
            </TextField.Slot>
          </TextField.Root>
          {total > 0 && (
            <Text size="2" color="gray" role="status">
              {t('welcome.showing', { shown: vehicles.length, total })}
            </Text>
          )}
        </Flex>
      )}
      {error && <ErrorMessage error={error} />}
      {!data && !error && (
        <Loading />
      )}
      {data && total === 0 && searching && <Text as="p">{t('welcome.noMatches', { search: input.trim() })}</Text>}
      {data && total === 0 && !searching && (
        <Flex direction="column" gap="3" align="start">
          <Text as="p">{t('welcome.empty')}</Text>
        </Flex>
      )}
      {vehicles.length > 0 && (
        <Grid asChild columns={{ initial: '1', sm: '2', xl: '3' }} gap="4">
          <ul aria-label={t('welcome.countLabel', { count: total })} style={{ listStyle: 'none', padding: 0, margin: 0 }}>
            <AnimatePresence initial={false}>
              {vehicles.map((v) => (
                <motion.li key={v.id} style={{ display: 'grid' }} {...itemMotion}>
                  <VehicleCard vehicle={v} />
                </motion.li>
              ))}
            </AnimatePresence>
          </ul>
        </Grid>
      )}
      {moreError !== undefined && <ErrorMessage error={moreError} />}
      {hasMore && (
        <Flex ref={sentinel} justify="center" mt="4">
          <Button size="3" variant="soft" loading={loadingMore} onClick={() => void loadMore()}>
            {t('welcome.more')}
          </Button>
        </Flex>
      )}
    </section>
  )
}
