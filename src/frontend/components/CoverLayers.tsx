import { placeholderGradient } from '../dashboard/chartFormat.ts'

/**
 * The background of a banner or card: the vehicle's picture (a decorative image, the vehicle's name is next to it) or a gradient,
 * under a dark scrim so white text stays readable. The parent must be `position: relative` with `overflow: hidden`.
 */
export function CoverLayers({ pictureUrl, id }: { pictureUrl?: string | null; id: string }) {
  return (
    <>
      <div className="cover" style={{ background: placeholderGradient(id) }} />
      {pictureUrl && <img className="cover" src={pictureUrl} alt="" loading="lazy" />}
      <div className="scrim" />
    </>
  )
}
