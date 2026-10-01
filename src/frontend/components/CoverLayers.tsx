import { placeholderGradient } from '../dashboard/chartFormat.ts'

/**
 * The background of a banner or card: the vehicle's picture (a CSS background, so it is cropped to fill the box and never stretched; it is decorative, the vehicle's name is next to it) or a gradient,
 * under a dark scrim so white text stays readable. The parent must be `position: relative` with `overflow: hidden`.
 */
export function CoverLayers({ pictureUrl, id }: { pictureUrl?: string | null; id: string }) {
  return (
    <>
      <div className="cover" style={{ background: placeholderGradient(id) }} />
      {pictureUrl && <div className="cover cover-picture" style={{ backgroundImage: `url("${pictureUrl.replace(/"/g, '%22')}")` }} />}
      <div className="scrim" />
    </>
  )
}
