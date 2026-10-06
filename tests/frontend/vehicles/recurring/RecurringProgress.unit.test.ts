import { expect, it } from 'vitest'
import { recurringProgress, type ScheduleProgressSource } from '../../../../src/frontend/recurringProgress.ts'

const schedule = (over: Omit<Partial<ScheduleProgressSource>, 'status'> & { status?: Partial<ScheduleProgressSource['status']> } = {}): ScheduleProgressSource => ({
  kind: 'TIME',
  intervalDistance: null,
  lastDoneDate: '2026-01-01',
  ...over,
  status: { limit: 'TIME', dueDate: '2026-12-27', daysLeft: 90, distanceLeft: null, ...over.status },
})

it('a time schedule: the share of the days between the last time and the due day that have passed', () => {
  expect(recurringProgress(schedule())).toEqual([{ limit: 'TIME', used: (360 - 90) / 360, deciding: true }])
})

it('a distance schedule: the share of the interval driven', () => {
  const progress = recurringProgress(schedule({ kind: 'ODOMETER', intervalDistance: 15000, status: { limit: 'ODOMETER', dueDate: null, daysLeft: null, distanceLeft: 3000 } }))
  expect(progress).toEqual([{ limit: 'ODOMETER', used: 0.8, deciding: true }])
})

it('a combined schedule has both, and the limit that decides is the one the server named', () => {
  const progress = recurringProgress(schedule({ kind: 'COMBINED', intervalDistance: 10000, status: { limit: 'ODOMETER', distanceLeft: 500 } }))
  expect(progress.map((p) => [p.limit, p.deciding])).toEqual([
    ['TIME', false],
    ['ODOMETER', true],
  ])
})

it('an overdue limit is past one; a limit without a reading yet is left out', () => {
  expect(recurringProgress(schedule({ status: { daysLeft: -36 } }))[0].used).toBeCloseTo(1.1)
  expect(recurringProgress(schedule({ kind: 'ODOMETER', intervalDistance: 15000, status: { limit: 'ODOMETER', dueDate: null, daysLeft: null, distanceLeft: null } }))).toEqual([])
})
