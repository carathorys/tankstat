/** Where the browser keeps the colour mode (light, dark or system): the `modeStorageKey` MUI is given; the account keeps it too (UiSettings). */
export const COLOR_MODE_KEY = 'tankstat.colorMode'

/** The choices, as MUI names them (the server's enum in capitals: LIGHT, DARK, SYSTEM). */
export const COLOR_MODES = ['light', 'dark', 'system'] as const
export type ColorModeChoice = (typeof COLOR_MODES)[number]
