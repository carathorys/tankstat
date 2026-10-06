import { Table } from '@radix-ui/themes'
import { motion, type Transition } from 'motion/react'

/**
 * Motion for items that come, go and move (the tk-* classes of the theme cover what only comes in). Short, and `MotionConfig
 * reducedMotion="user"` (main.tsx) leaves only the fade when the user asks for reduced motion. Wrap a list in `AnimatePresence` so a
 * removed item can leave (`initial={false}` there: items present on the first render do not animate in).
 */
export const QUICK: Transition = { duration: 0.18, ease: 'easeOut' }

/** A table row that fades in and out. */
export const MotionRow = motion.create(Table.Row)
export const rowMotion = { initial: { opacity: 0 }, animate: { opacity: 1 }, exit: { opacity: 0 }, transition: QUICK } as const

/** A list item that fades in and out and slides to its new place when the list is reordered. */
export const itemMotion = { layout: true, initial: { opacity: 0, scale: 0.98 }, animate: { opacity: 1, scale: 1 }, exit: { opacity: 0, scale: 0.98 }, transition: QUICK } as const
