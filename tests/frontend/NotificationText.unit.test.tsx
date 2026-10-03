import { renderHook } from '@testing-library/react'
import { expect, it } from 'vitest'
import type { NotificationKind } from '../../src/frontend/gql/generated.ts'
import { useNotificationText } from '../../src/frontend/notifications/useNotificationText.ts'
import { fakeNotification } from './mocks.tsx'

const describeNotification = renderHook(() => useNotificationText()).result.current

const say = (kind: NotificationKind, args: Record<string, string>, over: Parameters<typeof fakeNotification>[0] = {}) =>
  describeNotification(fakeNotification({ kind, args: Object.entries(args).map(([name, value]) => ({ name, value })), ...over }))

const people = { actorName: 'Root', userName: 'Bob', vehicleName: 'Family car' }

it('words access changes with the level they gave, and leads to the vehicle while there is access', () => {
  expect(say('LOG_ACCESS_CHANGED', { ...people, level: 'DELETE' })).toEqual({
    text: 'Root gave you access to the logs of Family car: Add, change and delete logs permanently.',
    href: '/vehicles/v1',
  })
  expect(say('LOG_ACCESS_CHANGED', { ...people, level: 'NONE' })).toEqual({ text: 'Root removed your access to the logs of Family car.', href: null })
  expect(say('VEHICLE_SHARED', { ...people, level: 'EDIT' }).text).toBe('Root gave Bob access to the logs of Family car: Add and change logs.')
  expect(say('VEHICLE_SHARED', { ...people, level: 'NONE' }).text).toBe('Root removed the access of Bob to the logs of Family car.')
  expect(say('DATA_ACCESS_CHANGED', { ...people, level: 'VIEW' }, { context: null }).text).toBe('Root let you access everything Bob owns: view.')
  expect(say('DATA_ACCESS_CHANGED', { ...people, level: 'NONE' }, { context: null }).text).toBe('Root removed your access to the data of Bob.')
  expect(say('DATA_SHARED', { ...people, level: 'EDIT' }, { context: null })).toEqual({ text: 'Root let Bob access everything you own: view and edit.', href: null })
  expect(say('DATA_SHARED', { ...people, level: 'NONE' }, { context: null }).text).toBe('Root removed the access of Bob to your data.')
  expect(say('DEFAULT_ACCESS_CHANGED', { actorName: 'Root', level: 'VIEW' }, { context: null }).text).toBe("Root changed what everyone may do with other users' data: view.")
})

it('says how many changes were folded into one notification', () => {
  expect(say('LOG_ACCESS_CHANGED', { ...people, level: 'EDIT' }, { count: 3 }).text).toBe('Root gave you access to the logs of Family car: Add and change logs. (3 changes)')
})

it('leads due schedules to the vehicle\'s recurring tab', () => {
  const args = { title: 'Oil change', vehicleName: 'Family car' }
  expect(say('RECURRING_DUE_SOON', args)).toEqual({ text: 'Oil change on Family car is due soon.', href: '/vehicles/v1?tab=recurring' })
  expect(say('RECURRING_OVERDUE', args).text).toBe('Oil change on Family car is overdue.')
})

it('counts what was too much to list', () => {
  expect(say('MORE_ACTIVITY', {}, { count: 1, context: null })).toEqual({ text: 'There was 1 more change, too many to list one by one.', href: null })
  expect(say('MORE_ACTIVITY', {}, { count: 7, context: null }).text).toBe('There were 7 more changes, too many to list one by one.')
})

it('tells what a photo filled in or left out, and leads to the right tab of the vehicle', () => {
  const log = { vehicleName: 'Family car', date: '2026-09-30' }
  expect(say('LOG_FILLED_FROM_PHOTO', { ...log, values: 'ODOMETER' }, { subject: { type: 'REFUELING', id: 'r1' } })).toEqual({
    text: 'The photo of the Sep 30, 2026 refuelling of Family car filled in: odometer. Check it.',
    href: '/vehicles/v1?tab=refuelings',
  })
  expect(say('LOG_NOT_FILLED', { ...log, values: 'VOLUME,TOTAL' }, { subject: { type: 'REFUELING', id: 'r1' } }).text).toBe(
    'The photos of the Sep 30, 2026 refuelling of Family car did not show: volume and total. Fill it in.',
  )
  expect(say('LOG_FILLED_FROM_PHOTO', { ...log, title: 'Service', values: 'TOTAL,ODOMETER' }, { subject: { type: 'EXPENSE', id: 'e1' } })).toEqual({
    text: 'The photo of Service (Sep 30, 2026, Family car) filled in: total and odometer. Check it.',
    href: '/vehicles/v1?tab=expenses',
  })
})
