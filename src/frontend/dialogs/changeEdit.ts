/**
 * Editing a change (on Waiting to sync: one kept on this device, or one the server parked) in the dialog of the entry it concerns: the
 * values the change carries, and the dialog's own title and Save label. The dialog loads nothing and offers no photos; the caller's
 * `onSubmit` decides what saving does (keep the change, or apply it on the server) and an error it throws stays in the dialog.
 */
export interface ChangeEdit<T> {
  initial: Partial<T>
  title: string
  submitLabel: string
}
