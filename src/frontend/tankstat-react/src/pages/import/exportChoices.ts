import type { ExportCarsQuery } from '../../gql/generated.ts'

/** A car of the person's own as the Export tab offers it. */
export type ExportOffer = ExportCarsQuery['exportCars'][number]

/** The kinds a car's export takes, as `startExport` names them. */
export const EXPORT_KINDS = ['refuelings', 'expenses', 'schedules', 'tyres', 'charts', 'photos', 'picture'] as const
export type ExportKind = (typeof EXPORT_KINDS)[number]
export type ExportKinds = Record<ExportKind, boolean>

export const ALL_KINDS: ExportKinds = { refuelings: true, expenses: true, schedules: true, tyres: true, charts: true, photos: true, picture: true }

/** The kinds a car has something of: a car without a picture offers none to tick. */
export const kindsOf = (car: ExportOffer): ExportKind[] => EXPORT_KINDS.filter((k) => k !== 'picture' || car.pictureBytes > 0)

/** Whether a car's choice takes anything (a kind it has nothing of does not count). */
export const takesAnything = (car: ExportOffer, kinds: ExportKinds) => kindsOf(car).some((k) => kinds[k])

/** About how large the file is: the rows as the server reckons them, and the photos' and the picture's own sizes when they go. */
export const estimate = (car: ExportOffer, kinds: ExportKinds) =>
  car.rowBytes + (kinds.photos ? car.photoBytes : 0) + (kinds.picture ? car.pictureBytes : 0)

/** The input `startExport` takes for a car: every kind named, so what the person sees is what is sent. */
export const inputOf = (car: ExportOffer, kinds: ExportKinds) => ({ vehicleId: car.id, ...Object.fromEntries(EXPORT_KINDS.map((k) => [k, kinds[k] && kindsOf(car).includes(k)])) })
