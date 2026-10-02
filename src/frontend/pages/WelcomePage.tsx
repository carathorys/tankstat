import { useMutation, useQuery } from '@apollo/client/react'
import { Button, Flex, Grid, Heading, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { VehicleCard } from '../components/VehicleCard.tsx'
import { AddVehicleDocument, WelcomeDocument } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage } from '../messages.tsx'
import { VehicleFormDialog } from '../VehicleFormDialog.tsx'

/**
 * The start page: a card for every vehicle the user owns or that is shared with them, with its picture and key figures. This is where
 * everyone adds vehicles; the full list is for administrators (`isAdmin`).
 */
export function WelcomePage({ isAdmin }: { isAdmin: boolean }) {
  const { t } = useTranslation()
  usePageTitle(t('nav.home'))
  const { data, error } = useQuery(WelcomeDocument, { fetchPolicy: 'cache-and-network' })
  const [addVehicle] = useMutation(AddVehicleDocument, { refetchQueries: ['Welcome'], awaitRefetchQueries: true })
  const addButton = (primary: boolean) => (
    <VehicleFormDialog
      trigger={<Button size="3" variant={primary ? 'solid' : 'soft'}>{t('welcome.add')}</Button>}
      onSubmit={(input) => addVehicle({ variables: { input } })}
    />
  )

  return (
    <section aria-labelledby="page-title">
      <Flex justify="between" align="center" gap="3" wrap="wrap" mb="4">
        <Heading id="page-title">{t('welcome.title')}</Heading>
        <Flex gap="2" wrap="wrap">
          <Button asChild size="3" variant="soft">
            <Link to="/import">{t('welcome.import')}</Link>
          </Button>
          {addButton(!isAdmin)}
          {isAdmin && (
            <Button asChild size="3">
              <Link to="/vehicles">{t('welcome.all')}</Link>
            </Button>
          )}
        </Flex>
      </Flex>
      {error && <ErrorMessage error={error} />}
      {!data && !error && (
        <Text as="p" role="status">
          {t('app.loading')}
        </Text>
      )}
      {data && data.myVehicles.length === 0 && (
        <Flex direction="column" gap="3" align="start">
          <Text as="p">{t('welcome.empty')}</Text>
          <Flex gap="2" wrap="wrap">
            <Button asChild size="3" variant="soft">
              <Link to="/import">{t('welcome.import')}</Link>
            </Button>
          </Flex>
        </Flex>
      )}
      {data && data.myVehicles.length > 0 && (
        <Grid asChild columns={{ initial: '1', sm: '2', lg: '3' }} gap="4">
          <ul aria-label={t('welcome.countLabel', { count: data.myVehicles.length })} style={{ listStyle: 'none', padding: 0, margin: 0 }}>
            {data.myVehicles.map((v) => (
              <li key={v.id}>
                <VehicleCard vehicle={v} />
              </li>
            ))}
          </ul>
        </Grid>
      )}
    </section>
  )
}
