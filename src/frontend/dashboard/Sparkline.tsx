import { useTranslation } from 'react-i18next'
import { Area, AreaChart, ResponsiveContainer } from 'recharts'
import { useFormat } from '../i18n/format.ts'

/**
 * A tiny trend line without axes. It is decorative: the same figures are in the accessible label. It is not animated: it usually sits in a
 * hidden or sliding panel (a draw-in animation that starts out of sight can stall half way), and the panel's own slide is the motion.
 */
export function Sparkline({ points, currency, height = 36 }: { points: { month: string; amount: number }[]; currency?: string | null; height?: number }) {
  const { t } = useTranslation()
  const format = useFormat()
  const label = `${t('welcome.card.trend')}: ${points.map((p) => (currency ? format.money(p.amount, currency) : String(p.amount))).join(', ')}`

  return (
    <div role="img" aria-label={label} style={{ height, width: '100%' }}>
      <div aria-hidden style={{ height: '100%' }}>
        <ResponsiveContainer width="100%" height={height} initialDimension={{ width: 120, height }}>
          <AreaChart data={points} margin={{ top: 2, right: 0, bottom: 2, left: 0 }} accessibilityLayer={false}>
            <Area type="monotone" dataKey="amount" stroke="var(--accent-9)" fill="var(--accent-9)" fillOpacity={0.25} strokeWidth={2} isAnimationActive={false} />
          </AreaChart>
        </ResponsiveContainer>
      </div>
    </div>
  )
}
