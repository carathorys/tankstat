import { expect, it } from 'vitest'
import { restoreRisks } from '../../../src/frontend/admin/restoreRisks.ts'

const file = { __typename: 'BackupSettingsFileInfo' as const, state: 'INCLUDED' as const, secrets: true, secretsLeftOut: [] as string[] }
const environment = { __typename: 'BackupEnvironmentInfo' as const, mode: 'RECORD' as const, settings: ['Auth__Mode'], missing: [] as string[], missingCertain: true, logLevelsMissing: [] as string[], overridden: [] as string[] }

it('a backup of this release with its secrets, nothing missing and nothing overridden, carries no risk', () => {
  expect(restoreRisks({ settingsFile: file, environment })).toEqual([])
})

it('what this server lacks refuses the restore; it is only a risk where an encrypted file may still hold it', () => {
  expect(restoreRisks({ settingsFile: file, environment: { ...environment, missing: ['Smtp__Host', 'Smtp__Port'] } })).toEqual([
    { tier: 'blocker', key: 'missing', names: 'Smtp__Host, Smtp__Port' },
  ])
  expect(restoreRisks({ settingsFile: file, environment: { ...environment, missing: ['Smtp__Host'], missingCertain: false } })).toEqual([
    { tier: 'risk', key: 'mayBeMissing', names: 'Smtp__Host' },
  ])
})

it('a log level this server lacks is only a risk: it never refuses the restore', () => {
  expect(restoreRisks({ settingsFile: file, environment: { ...environment, logLevelsMissing: ['Logging__LogLevel__Default'] } })).toEqual([
    { tier: 'risk', key: 'logLevelsMissing', names: 'Logging__LogLevel__Default' },
  ])
})

it('no settings file, and why; its secrets left out; settings this server overrides, the sign-in mode on its own', () => {
  expect(restoreRisks({ settingsFile: null, environment: null })).toEqual([{ tier: 'risk', key: 'noSettingsOlder' }])
  expect(restoreRisks({ settingsFile: { ...file, state: 'NONE' }, environment }).map((r) => r.key)).toEqual(['noSettingsNone'])
  expect(restoreRisks({ settingsFile: { ...file, state: 'IGNORED' }, environment }).map((r) => r.key)).toEqual(['noSettingsIgnored'])
  expect(restoreRisks({ settingsFile: { ...file, secrets: false, secretsLeftOut: ['Smtp:Password'] }, environment })).toEqual([
    { tier: 'risk', key: 'secretsLeftOut', names: 'Smtp:Password' },
  ])
  expect(restoreRisks({ settingsFile: file, environment: { ...environment, overridden: ['Auth__Mode', 'Defaults__Currency'] } })).toEqual([
    { tier: 'risk', key: 'signInOverridden' },
    { tier: 'risk', key: 'overridden', names: 'Defaults__Currency' },
  ])
  expect(restoreRisks({ settingsFile: file, environment: null })).toEqual([{ tier: 'risk', key: 'environmentUnknown' }])
})
