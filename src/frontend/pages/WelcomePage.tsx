import { useQuery } from '@apollo/client/react'
import { Button, Flex, Grid, Heading, Text } from '@radix-ui/themes'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { VehicleCard } from '../components/VehicleCard.tsx'
import { WelcomeDocument } from '../gql/generated.ts'
import { usePageTitle } from '../hooks/usePageTitle.ts'
import { ErrorMessage } from '../messages.tsx'

/** The start page: a card for every vehicle the user can see, with its picture and key figures. */
export function WelcomePage() {
  const { t } = useTranslation()
  usePageTitle(t('nav.home'))
  const { data, error } = useQuery(WelcomeDocument, { fetchPolicy: 'cache-and-network' })

  return (
    <section aria-labelledby="page-title">
      <Flex justify="between" align="center" gap="3" wrap="wrap" mb="4">
        <Heading id="page-title">{t('welcome.title')}</Heading>
        <Flex gap="2" wrap="wrap">
          <Button asChild size="3" variant="soft">
            <Link to="/import">{t('welcome.import')}</Link>
          </Button>
          <Button asChild size="3">
            <Link to="/vehicles">{t('welcome.all')}</Link>
          </Button>
        </Flex>
      </Flex>
      {error && <ErrorMessage error={error} />}
      {!data && !error && (
        <Text as="p" role="status">
          {t('app.loading')}
        </Text>
      )}
      {data && data.vehicles.length === 0 && (
        <Flex direction="column" gap="3" align="start">
          <Text as="p">{t('welcome.empty')}</Text>
          <Flex gap="2" wrap="wrap">
            <Button asChild size="3">
              <Link to="/vehicles">{t('welcome.add')}</Link>
            </Button>
            <Button asChild size="3" variant="soft">
              <Link to="/import">{t('welcome.import')}</Link>
            </Button>
          </Flex>
        </Flex>
      )}
      {data && data.vehicles.length > 0 && (
        <Grid asChild columns={{ initial: '1', sm: '2', lg: '3' }} gap="4">
          <ul aria-label={t('welcome.countLabel', { count: data.vehicleCount })} style={{ listStyle: 'none', padding: 0, margin: 0 }}>
            {data.vehicles.map((v) => (
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
