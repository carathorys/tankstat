/** Internal type. DO NOT USE DIRECTLY. */
type Exact<T extends { [key: string]: unknown }> = { [K in keyof T]: T[K] };
/** Internal type. DO NOT USE DIRECTLY. */
export type Incremental<T> = T | { [P in keyof T]?: P extends ' $fragmentName' | '__typename' ? T[P] : never };
import type { TypedDocumentNode as DocumentNode } from '@graphql-typed-document-node/core';
export type AccessLevel =
  | 'DELETE'
  | 'EDIT'
  | 'NONE'
  | 'VIEW';

export type AddExpenseInput = {
  amount: number;
  category?: string | null | undefined;
  currency?: string | null | undefined;
  date: string;
  note?: string | null | undefined;
  odometer?: number | null | undefined;
  title: string;
  vehicleId: string;
};

export type AddRecurringExpenseInput = {
  category?: string | null | undefined;
  intervalDistance?: number | null | undefined;
  intervalMonths?: number | null | undefined;
  kind: RecurrenceKind;
  lastDoneDate?: string | null | undefined;
  lastDoneOdometer?: number | null | undefined;
  note?: string | null | undefined;
  title: string;
  vehicleId: string;
  warnDays?: number | null | undefined;
  warnDistance?: number | null | undefined;
};

export type AddVehicleInput = {
  fuelType: FuelType;
  licensePlate?: string | null | undefined;
  name: string;
  units?: MeasurementUnitsInput | null | undefined;
};

export type AuthMode =
  | 'NONE'
  | 'OIDC'
  | 'PROXY_HEADER'
  | 'STANDALONE';

export type ChangePasswordInput = {
  currentPassword: string;
  newPassword: string;
};

export type ChartConfigInput = {
  from?: string | null | undefined;
  grouping: ChartGrouping;
  kind: ChartKind;
  metric: ChartMetric;
  range: ChartRange;
  stacked: boolean;
  to?: string | null | undefined;
};

export type ChartGrouping =
  | 'CATEGORY'
  | 'MONTH'
  | 'QUARTER'
  | 'YEAR';

export type ChartKind =
  | 'AREA'
  | 'BAR'
  | 'DONUT'
  | 'LINE';

export type ChartMetric =
  | 'AVERAGE_CONSUMPTION'
  | 'AVERAGE_PRICE_PER_UNIT'
  | 'DISTANCE'
  | 'EXPENSE_COST'
  | 'FILL_UPS'
  | 'FUEL_COST'
  | 'FUEL_VOLUME'
  | 'TOTAL_SPEND';

export type ChartRange =
  | 'ALL'
  | 'CUSTOM'
  | 'LAST1_MONTH'
  | 'LAST3_MONTHS'
  | 'LAST6_MONTHS'
  | 'LAST12_MONTHS'
  | 'LAST_YEAR'
  | 'THIS_YEAR';

export type ChartUnit =
  | 'CONSUMPTION'
  | 'COUNT'
  | 'CURRENCY'
  | 'DISTANCE'
  | 'VOLUME';

export type ConfirmImportInput = {
  currency?: string | null | undefined;
  importDuplicates: boolean;
  newVehicle?: NewVehicleInput | null | undefined;
  token: string;
  vehicleId?: string | null | undefined;
};

export type CreateUserInput = {
  displayName?: string | null | undefined;
  email: string;
  isAdmin: boolean;
};

export type DeleteUserInput = {
  data?: UserDataDisposition | null | undefined;
  moveToUserId?: string | null | undefined;
  userId: string;
};

export type DistanceUnit =
  | 'KILOMETERS'
  | 'MILES';

export type ExpenseSortField =
  | 'AMOUNT'
  | 'CATEGORY'
  | 'CREATED_BY'
  | 'DATE'
  | 'DELETED_AT'
  | 'ODOMETER'
  | 'TITLE'
  | 'VEHICLE';

export type FuelType =
  | 'DIESEL'
  | 'LPG'
  | 'PETROL';

export type LogRefuelingInput = {
  currency?: string | null | undefined;
  date: string;
  isFullTank: boolean;
  note?: string | null | undefined;
  odometer: number;
  totalCost: number;
  vehicleId: string;
  volume: number;
};

export type LoginInput = {
  email: string;
  password: string;
};

export type MarkRecurringExpenseDoneInput = {
  amount?: number | null | undefined;
  createExpense: boolean;
  currency?: string | null | undefined;
  date: string;
  id: string;
  odometer?: number | null | undefined;
};

export type MeasurementUnitsInput = {
  distance: DistanceUnit;
  volume: VolumeUnit;
};

export type NewVehicleInput = {
  fuelType: FuelType;
  licensePlate?: string | null | undefined;
  name: string;
  units: MeasurementUnitsInput;
};

export type NoticeSeverity =
  | 'INFO'
  | 'WARNING';

export type RecurrenceKind =
  | 'COMBINED'
  | 'ODOMETER'
  | 'TIME';

export type RecurrenceLimit =
  | 'ODOMETER'
  | 'TIME';

export type RecurrenceState =
  | 'DUE_SOON'
  | 'OVERDUE'
  | 'UPCOMING';

export type RefuelingSortField =
  | 'CONSUMPTION'
  | 'CREATED_BY'
  | 'DATE'
  | 'DELETED_AT'
  | 'ODOMETER'
  | 'PRICE_PER_UNIT'
  | 'TOTAL_COST'
  | 'VEHICLE'
  | 'VOLUME';

export type ResetPasswordInput = {
  newPassword: string;
  token: string;
};

export type SaveChartInput = {
  config: ChartConfigInput;
  id?: string | null | undefined;
  shared: boolean;
  title: string;
  vehicleId: string;
};

export type SetAccessGrantInput = {
  granteeId: string;
  level: AccessLevel;
  ownerId: string;
};

export type SetLogAccessInput = {
  level: AccessLevel;
  userId: string;
  vehicleId: string;
};

export type SortDirection =
  | 'ASC'
  | 'DESC';

export type UpdateExpenseInput = {
  amount: number;
  category?: string | null | undefined;
  currency?: string | null | undefined;
  date: string;
  id: string;
  note?: string | null | undefined;
  odometer?: number | null | undefined;
  title: string;
};

export type UpdateRecurringExpenseInput = {
  category?: string | null | undefined;
  id: string;
  intervalDistance?: number | null | undefined;
  intervalMonths?: number | null | undefined;
  kind: RecurrenceKind;
  lastDoneDate: string;
  lastDoneOdometer?: number | null | undefined;
  note?: string | null | undefined;
  title: string;
  warnDays?: number | null | undefined;
  warnDistance?: number | null | undefined;
};

export type UpdateRefuelingInput = {
  currency?: string | null | undefined;
  date: string;
  id: string;
  isFullTank: boolean;
  note?: string | null | undefined;
  odometer: number;
  totalCost: number;
  volume: number;
};

export type UpdateUserInput = {
  displayName?: string | null | undefined;
  email: string;
  userId: string;
};

export type UpdateVehicleInput = {
  fuelType: FuelType;
  id: string;
  licensePlate?: string | null | undefined;
  name: string;
  units?: MeasurementUnitsInput | null | undefined;
};

export type UserDataDisposition =
  | 'MOVE'
  | 'PURGE';

export type UserProvider =
  | 'LOCAL'
  | 'OIDC'
  | 'PROXY';

export type VehicleSortField =
  | 'DELETED_AT'
  | 'FUEL_TYPE'
  | 'LICENSE_PLATE'
  | 'NAME'
  | 'OWNER'
  | 'REFUELING_COUNT';

export type VolumeUnit =
  | 'IMPERIAL_GALLONS'
  | 'LITERS'
  | 'US_GALLONS';

export type AdminQueryVariables = Exact<{ [key: string]: never; }>;


export type AdminQuery = { canSetUserPasswords: boolean, users: Array<{ id: string, provider: UserProvider, email: string, displayName: string, isAdmin: boolean, isDisabled: boolean, avatarUrl: string | null }>, accessSettings: { defaultLevelForOthers: AccessLevel }, accessGrants: Array<{ id: string, ownerId: string, granteeId: string, level: AccessLevel }> };

export type CreateUserMutationVariables = Exact<{
  input: CreateUserInput;
}>;


export type CreateUserMutation = { createUser: { user: { id: string }, reset: { token: string, url: string | null, emailSent: boolean } } };

export type IssuePasswordResetMutationVariables = Exact<{
  userId: string;
}>;


export type IssuePasswordResetMutation = { issuePasswordReset: { token: string, url: string | null, emailSent: boolean } };

export type SetUserAdminMutationVariables = Exact<{
  userId: string;
  isAdmin: boolean;
}>;


export type SetUserAdminMutation = { setUserAdmin: { id: string } };

export type SetUserDisabledMutationVariables = Exact<{
  userId: string;
  disabled: boolean;
}>;


export type SetUserDisabledMutation = { setUserDisabled: { id: string } };

export type SetDefaultAccessMutationVariables = Exact<{
  level: AccessLevel;
}>;


export type SetDefaultAccessMutation = { setDefaultAccess: { defaultLevelForOthers: AccessLevel } };

export type SetAccessGrantMutationVariables = Exact<{
  input: SetAccessGrantInput;
}>;


export type SetAccessGrantMutation = { setAccessGrant: boolean };

export type UpdateUserMutationVariables = Exact<{
  input: UpdateUserInput;
}>;


export type UpdateUserMutation = { updateUser: { id: string } };

export type SetUserPasswordMutationVariables = Exact<{
  userId: string;
  newPassword: string;
}>;


export type SetUserPasswordMutation = { setUserPassword: boolean };

export type DeleteUserMutationVariables = Exact<{
  input: DeleteUserInput;
}>;


export type DeleteUserMutation = { deleteUser: boolean };

export type WelcomeQueryVariables = Exact<{
  search?: string | null | undefined;
  skip: number;
  take: number;
}>;


export type WelcomeQuery = { myVehicleCount: number, myVehicles: Array<{ id: string, name: string, licensePlate: string | null, fuelType: FuelType, pictureUrl: string | null, canEdit: boolean, logAccess: AccessLevel, units: { distance: DistanceUnit, volume: VolumeUnit }, owner: { id: string, displayName: string, avatarUrl: string | null } | null, recurring: Array<{ id: string, title: string, kind: RecurrenceKind, status: { state: RecurrenceState, limit: RecurrenceLimit | null, dueDate: string | null, daysLeft: number | null, distanceLeft: number | null } }>, summary: { lastFillUpDate: string | null, latestOdometer: number | null, averageConsumption: number | null, currency: string | null, thisMonthSpend: number, lastMonthSpend: number, fillUpCount: number, expenseCount: number, spendTrend: Array<{ month: string, amount: number }> } | null }> };

export type VehicleDashboardQueryVariables = Exact<{
  id: string;
}>;


export type VehicleDashboardQuery = { vehicle: { id: string, summary: { lastFillUpDate: string | null, latestOdometer: number | null, averageConsumption: number | null, currency: string | null, thisMonthSpend: number, lastMonthSpend: number, fillUpCount: number, expenseCount: number, spendTrend: Array<{ month: string, amount: number }> } | null } | null, vehicleCharts: Array<{ id: string, title: string, metric: ChartMetric, grouping: ChartGrouping, kind: ChartKind, range: ChartRange, rangeFrom: string | null, rangeTo: string | null, stacked: boolean, isShared: boolean, canEdit: boolean, createdBy: { id: string, displayName: string, avatarUrl: string | null } | null }> };

export type ChartDataQueryVariables = Exact<{
  vehicleId: string;
  config: ChartConfigInput;
}>;


export type ChartDataQuery = { vehicleChartData: { unit: ChartUnit, series: Array<{ kind: string, currency: string | null, points: Array<{ key: string, value: number | null }> }> } };

export type SaveChartMutationVariables = Exact<{
  input: SaveChartInput;
}>;


export type SaveChartMutation = { saveVehicleChart: { id: string } };

export type DeleteChartMutationVariables = Exact<{
  id: string;
}>;


export type DeleteChartMutation = { deleteVehicleChart: boolean };

export type ExpensesQueryVariables = Exact<{
  vehicleId: string;
  orderBy: ExpenseSortField;
  direction: SortDirection;
  skip: number;
  take: number;
  withCategory: boolean;
  withAmount: boolean;
  withOdometer: boolean;
  withNote: boolean;
  withCreatedBy: boolean;
}>;


export type ExpensesQuery = { expenseCount: number, expenses: Array<{ id: string, date: string, title: string, canEdit: boolean, canDelete: boolean, category?: string | null, amount?: number, currency?: string, odometer?: number | null, note?: string | null, createdBy?: { id: string, displayName: string, avatarUrl: string | null } | null }> };

export type ExpenseTrashQueryVariables = Exact<{
  orderBy: ExpenseSortField;
  direction: SortDirection;
  skip: number;
  take: number;
  withCategory: boolean;
  withAmount: boolean;
  withDeletedAt: boolean;
  withCreatedBy: boolean;
}>;


export type ExpenseTrashQuery = { expenseTrashCount: number, expenseTrashDeletableCount: number, expenseTrash: Array<{ id: string, date: string, title: string, category?: string | null, amount?: number, currency?: string, deletedAt?: string | null, vehicle: { id: string, name: string } | null, createdBy?: { id: string, displayName: string, avatarUrl: string | null } | null }> };

export type ExpenseDetailsQueryVariables = Exact<{
  id: string;
}>;


export type ExpenseDetailsQuery = { expense: { id: string, vehicleId: string, date: string, title: string, category: string | null, amount: number, currency: string, odometer: number | null, note: string | null, photos: Array<{ id: string, url: string }> } | null };

export type ExpenseCategoriesQueryVariables = Exact<{
  vehicleId: string;
}>;


export type ExpenseCategoriesQuery = { expenseCategories: Array<string> };

export type AddExpenseMutationVariables = Exact<{
  input: AddExpenseInput;
}>;


export type AddExpenseMutation = { addExpense: { id: string } };

export type UpdateExpenseMutationVariables = Exact<{
  input: UpdateExpenseInput;
}>;


export type UpdateExpenseMutation = { updateExpense: { id: string } };

export type DeleteExpenseMutationVariables = Exact<{
  id: string;
}>;


export type DeleteExpenseMutation = { deleteExpense: { id: string } };

export type RestoreExpenseMutationVariables = Exact<{
  id: string;
}>;


export type RestoreExpenseMutation = { restoreExpense: { id: string } };

export type EmptyExpenseTrashMutationVariables = Exact<{ [key: string]: never; }>;


export type EmptyExpenseTrashMutation = { emptyExpenseTrash: number };

export type HealthQueryVariables = Exact<{ [key: string]: never; }>;


export type HealthQuery = { health: { status: string, version: string, databaseReachable: boolean } };

export type ImportPreviewQueryVariables = Exact<{
  token: string;
  vehicleId?: string | null | undefined;
}>;


export type ImportPreviewQuery = { importPreview: { fuelRows: number, expenseRows: number, recurringRows: number, duplicateFuelRows: number, duplicateExpenseRows: number, duplicateRecurringRows: number, firstDate: string | null, lastDate: string | null, categories: Array<string>, sourceVehicle: { name: string | null, licensePlate: string | null, fuelType: FuelType | null, distanceUnit: DistanceUnit | null, volumeUnit: VolumeUnit | null } | null, issues: Array<{ section: string, row: number, key: string, args: unknown }> } };

export type ImportTargetsQueryVariables = Exact<{ [key: string]: never; }>;


export type ImportTargetsQuery = { myVehicles: Array<{ id: string, name: string, licensePlate: string | null, canEdit: boolean, logAccess: AccessLevel, units: { distance: DistanceUnit, volume: VolumeUnit } }> };

export type ConfirmImportMutationVariables = Exact<{
  input: ConfirmImportInput;
}>;


export type ConfirmImportMutation = { confirmImport: { vehicleId: string, fuelImported: number, expensesImported: number, recurringImported: number, fuelSkippedDuplicates: number, expensesSkippedDuplicates: number, recurringSkippedDuplicates: number, errors: Array<{ section: string, row: number, key: string, args: unknown }> } };

export type RecurringFieldsFragment = { id: string, title: string, category: string | null, note: string | null, kind: RecurrenceKind, intervalMonths: number | null, intervalDistance: number | null, lastDoneDate: string, lastDoneOdometer: number | null, warnDays: number, warnDistance: number, status: { state: RecurrenceState, limit: RecurrenceLimit | null, dueDate: string | null, dueOdometer: number | null, daysLeft: number | null, distanceLeft: number | null } };

export type RecurringExpensesQueryVariables = Exact<{
  vehicleId: string;
}>;


export type RecurringExpensesQuery = { vehicle: { id: string, recurring: Array<{ id: string, title: string, category: string | null, note: string | null, kind: RecurrenceKind, intervalMonths: number | null, intervalDistance: number | null, lastDoneDate: string, lastDoneOdometer: number | null, warnDays: number, warnDistance: number, status: { state: RecurrenceState, limit: RecurrenceLimit | null, dueDate: string | null, dueOdometer: number | null, daysLeft: number | null, distanceLeft: number | null } }> } | null };

export type AddRecurringExpenseMutationVariables = Exact<{
  input: AddRecurringExpenseInput;
}>;


export type AddRecurringExpenseMutation = { addRecurringExpense: { id: string, title: string, category: string | null, note: string | null, kind: RecurrenceKind, intervalMonths: number | null, intervalDistance: number | null, lastDoneDate: string, lastDoneOdometer: number | null, warnDays: number, warnDistance: number, status: { state: RecurrenceState, limit: RecurrenceLimit | null, dueDate: string | null, dueOdometer: number | null, daysLeft: number | null, distanceLeft: number | null } } };

export type UpdateRecurringExpenseMutationVariables = Exact<{
  input: UpdateRecurringExpenseInput;
}>;


export type UpdateRecurringExpenseMutation = { updateRecurringExpense: { id: string, title: string, category: string | null, note: string | null, kind: RecurrenceKind, intervalMonths: number | null, intervalDistance: number | null, lastDoneDate: string, lastDoneOdometer: number | null, warnDays: number, warnDistance: number, status: { state: RecurrenceState, limit: RecurrenceLimit | null, dueDate: string | null, dueOdometer: number | null, daysLeft: number | null, distanceLeft: number | null } } };

export type DeleteRecurringExpenseMutationVariables = Exact<{
  id: string;
}>;


export type DeleteRecurringExpenseMutation = { deleteRecurringExpense: boolean };

export type MarkRecurringExpenseDoneMutationVariables = Exact<{
  input: MarkRecurringExpenseDoneInput;
}>;


export type MarkRecurringExpenseDoneMutation = { markRecurringExpenseDone: { id: string, title: string, category: string | null, note: string | null, kind: RecurrenceKind, intervalMonths: number | null, intervalDistance: number | null, lastDoneDate: string, lastDoneOdometer: number | null, warnDays: number, warnDistance: number, status: { state: RecurrenceState, limit: RecurrenceLimit | null, dueDate: string | null, dueOdometer: number | null, daysLeft: number | null, distanceLeft: number | null } } };

export type RefuelingsQueryVariables = Exact<{
  vehicleId: string;
  orderBy: RefuelingSortField;
  direction: SortDirection;
  skip: number;
  take: number;
  withVolume: boolean;
  withCost: boolean;
  withPrice: boolean;
  withConsumption: boolean;
  withOdometer: boolean;
  withFullTank: boolean;
  withNote: boolean;
  withCreatedBy: boolean;
}>;


export type RefuelingsQuery = { refuelingCount: number, refuelings: Array<{ id: string, date: string, canEdit: boolean, canDelete: boolean, volume?: number, totalCost?: number, currency?: string, pricePerUnit?: number, consumption?: number | null, odometer?: number, isFullTank?: boolean, note?: string | null, createdBy?: { id: string, displayName: string, avatarUrl: string | null } | null }> };

export type RefuelingTrashQueryVariables = Exact<{
  orderBy: RefuelingSortField;
  direction: SortDirection;
  skip: number;
  take: number;
  withVolume: boolean;
  withCost: boolean;
  withOdometer: boolean;
  withDeletedAt: boolean;
  withCreatedBy: boolean;
}>;


export type RefuelingTrashQuery = { refuelingTrashCount: number, refuelingTrashDeletableCount: number, refuelingTrash: Array<{ id: string, date: string, volume?: number, totalCost?: number, currency?: string, odometer?: number, deletedAt?: string | null, vehicle: { id: string, name: string, units: { distance: DistanceUnit, volume: VolumeUnit } } | null, createdBy?: { id: string, displayName: string, avatarUrl: string | null } | null }> };

export type RefuelingDetailsQueryVariables = Exact<{
  id: string;
}>;


export type RefuelingDetailsQuery = { refueling: { id: string, vehicleId: string, date: string, volume: number, totalCost: number, currency: string, odometer: number, isFullTank: boolean, note: string | null, photos: Array<{ id: string, url: string }> } | null };

export type LogDefaultsQueryVariables = Exact<{
  vehicleId: string;
}>;


export type LogDefaultsQuery = { logDefaults: { lastOdometer: number | null, lastDate: string | null, currency: string } | null };

export type LogRefuelingMutationVariables = Exact<{
  input: LogRefuelingInput;
}>;


export type LogRefuelingMutation = { logRefueling: { id: string } };

export type UpdateRefuelingMutationVariables = Exact<{
  input: UpdateRefuelingInput;
}>;


export type UpdateRefuelingMutation = { updateRefueling: { id: string } };

export type DeleteRefuelingMutationVariables = Exact<{
  id: string;
}>;


export type DeleteRefuelingMutation = { deleteRefueling: { id: string } };

export type RestoreRefuelingMutationVariables = Exact<{
  id: string;
}>;


export type RestoreRefuelingMutation = { restoreRefueling: { id: string } };

export type EmptyRefuelingTrashMutationVariables = Exact<{ [key: string]: never; }>;


export type EmptyRefuelingTrashMutation = { emptyRefuelingTrash: number };

export type SessionQueryVariables = Exact<{ [key: string]: never; }>;


export type SessionQuery = { session: { mode: AuthMode, user: { id: string, displayName: string, email: string, isAdmin: boolean, avatarUrl: string | null } | null }, notices: Array<{ code: string, severity: NoticeSeverity, message: string }> };

export type LoginMutationVariables = Exact<{
  input: LoginInput;
}>;


export type LoginMutation = { login: { id: string } };

export type LogoutMutationVariables = Exact<{ [key: string]: never; }>;


export type LogoutMutation = { logout: boolean };

export type ChangePasswordMutationVariables = Exact<{
  input: ChangePasswordInput;
}>;


export type ChangePasswordMutation = { changePassword: boolean };

export type RequestPasswordResetMutationVariables = Exact<{
  email: string;
}>;


export type RequestPasswordResetMutation = { requestPasswordReset: boolean };

export type ResetPasswordMutationVariables = Exact<{
  input: ResetPasswordInput;
}>;


export type ResetPasswordMutation = { resetPassword: boolean };

export type LogAccessQueryVariables = Exact<{
  vehicleId: string;
}>;


export type LogAccessQuery = { vehicleLogAccess: Array<{ level: AccessLevel, user: { id: string, displayName: string, avatarUrl: string | null } }>, shareCandidates: Array<{ id: string, displayName: string, avatarUrl: string | null }> };

export type SetVehicleLogAccessMutationVariables = Exact<{
  input: SetLogAccessInput;
}>;


export type SetVehicleLogAccessMutation = { setVehicleLogAccess: boolean };

export type VehiclesQueryVariables = Exact<{
  orderBy: VehicleSortField;
  direction: SortDirection;
  skip: number;
  take: number;
  withLicensePlate: boolean;
  withFuelType: boolean;
  withOwner: boolean;
  withRefuelings: boolean;
}>;


export type VehiclesQuery = { vehicleCount: number, vehicles: Array<{ id: string, name: string, canEdit: boolean, pictureUrl: string | null, licensePlate?: string | null, fuelType?: FuelType, refuelingCount?: number, owner?: { id: string, displayName: string, avatarUrl: string | null } | null }> };

export type TrashQueryVariables = Exact<{
  orderBy: VehicleSortField;
  direction: SortDirection;
  skip: number;
  take: number;
  withLicensePlate: boolean;
  withFuelType: boolean;
  withOwner: boolean;
  withDeletedAt: boolean;
}>;


export type TrashQuery = { trashCount: number, trashDeletableCount: number, trash: Array<{ id: string, name: string, pictureUrl: string | null, licensePlate?: string | null, fuelType?: FuelType, deletedAt?: string | null, owner?: { id: string, displayName: string, avatarUrl: string | null } | null }> };

export type VehicleDetailsQueryVariables = Exact<{
  id: string;
}>;


export type VehicleDetailsQuery = { vehicle: { id: string, name: string, licensePlate: string | null, fuelType: FuelType, pictureUrl: string | null, canEdit: boolean, logAccess: AccessLevel, refuelingCount: number, units: { distance: DistanceUnit, volume: VolumeUnit }, owner: { id: string, displayName: string, avatarUrl: string | null } | null } | null };

export type VehicleDefaultsQueryVariables = Exact<{ [key: string]: never; }>;


export type VehicleDefaultsQuery = { vehicleDefaults: { distanceUnit: DistanceUnit, volumeUnit: VolumeUnit, currency: string } };

export type AddVehicleMutationVariables = Exact<{
  input: AddVehicleInput;
}>;


export type AddVehicleMutation = { addVehicle: { id: string } };

export type UpdateVehicleMutationVariables = Exact<{
  input: UpdateVehicleInput;
}>;


export type UpdateVehicleMutation = { updateVehicle: { id: string } };

export type DeleteVehicleMutationVariables = Exact<{
  id: string;
}>;


export type DeleteVehicleMutation = { deleteVehicle: { id: string } };

export type RestoreVehicleMutationVariables = Exact<{
  id: string;
}>;


export type RestoreVehicleMutation = { restoreVehicle: { id: string } };

export type EmptyTrashMutationVariables = Exact<{ [key: string]: never; }>;


export type EmptyTrashMutation = { emptyTrash: number };

export const RecurringFieldsFragmentDoc = {"kind":"Document","definitions":[{"kind":"FragmentDefinition","name":{"kind":"Name","value":"RecurringFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"RecurringExpenseInfo"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"category"}},{"kind":"Field","name":{"kind":"Name","value":"note"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"intervalMonths"}},{"kind":"Field","name":{"kind":"Name","value":"intervalDistance"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneDate"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"warnDays"}},{"kind":"Field","name":{"kind":"Name","value":"warnDistance"}},{"kind":"Field","name":{"kind":"Name","value":"status"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"limit"}},{"kind":"Field","name":{"kind":"Name","value":"dueDate"}},{"kind":"Field","name":{"kind":"Name","value":"dueOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"daysLeft"}},{"kind":"Field","name":{"kind":"Name","value":"distanceLeft"}}]}}]}}]} as unknown as DocumentNode<RecurringFieldsFragment, unknown>;
export const AdminDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Admin"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"users"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"provider"}},{"kind":"Field","name":{"kind":"Name","value":"email"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"isAdmin"}},{"kind":"Field","name":{"kind":"Name","value":"isDisabled"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}},{"kind":"Field","name":{"kind":"Name","value":"canSetUserPasswords"}},{"kind":"Field","name":{"kind":"Name","value":"accessSettings"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"defaultLevelForOthers"}}]}},{"kind":"Field","name":{"kind":"Name","value":"accessGrants"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"ownerId"}},{"kind":"Field","name":{"kind":"Name","value":"granteeId"}},{"kind":"Field","name":{"kind":"Name","value":"level"}}]}}]}}]} as unknown as DocumentNode<AdminQuery, AdminQueryVariables>;
export const CreateUserDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"CreateUser"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"CreateUserInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"createUser"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"user"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}},{"kind":"Field","name":{"kind":"Name","value":"reset"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"token"}},{"kind":"Field","name":{"kind":"Name","value":"url"}},{"kind":"Field","name":{"kind":"Name","value":"emailSent"}}]}}]}}]}}]} as unknown as DocumentNode<CreateUserMutation, CreateUserMutationVariables>;
export const IssuePasswordResetDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"IssuePasswordReset"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"userId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"issuePasswordReset"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"userId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"userId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"token"}},{"kind":"Field","name":{"kind":"Name","value":"url"}},{"kind":"Field","name":{"kind":"Name","value":"emailSent"}}]}}]}}]} as unknown as DocumentNode<IssuePasswordResetMutation, IssuePasswordResetMutationVariables>;
export const SetUserAdminDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetUserAdmin"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"userId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"isAdmin"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setUserAdmin"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"userId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"userId"}}},{"kind":"Argument","name":{"kind":"Name","value":"isAdmin"},"value":{"kind":"Variable","name":{"kind":"Name","value":"isAdmin"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<SetUserAdminMutation, SetUserAdminMutationVariables>;
export const SetUserDisabledDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetUserDisabled"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"userId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"disabled"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setUserDisabled"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"userId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"userId"}}},{"kind":"Argument","name":{"kind":"Name","value":"disabled"},"value":{"kind":"Variable","name":{"kind":"Name","value":"disabled"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<SetUserDisabledMutation, SetUserDisabledMutationVariables>;
export const SetDefaultAccessDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetDefaultAccess"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"level"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"AccessLevel"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setDefaultAccess"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"level"},"value":{"kind":"Variable","name":{"kind":"Name","value":"level"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"defaultLevelForOthers"}}]}}]}}]} as unknown as DocumentNode<SetDefaultAccessMutation, SetDefaultAccessMutationVariables>;
export const SetAccessGrantDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetAccessGrant"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SetAccessGrantInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setAccessGrant"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}]}]}}]} as unknown as DocumentNode<SetAccessGrantMutation, SetAccessGrantMutationVariables>;
export const UpdateUserDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateUser"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UpdateUserInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateUser"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<UpdateUserMutation, UpdateUserMutationVariables>;
export const SetUserPasswordDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetUserPassword"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"userId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"newPassword"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setUserPassword"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"userId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"userId"}}},{"kind":"Argument","name":{"kind":"Name","value":"newPassword"},"value":{"kind":"Variable","name":{"kind":"Name","value":"newPassword"}}}]}]}}]} as unknown as DocumentNode<SetUserPasswordMutation, SetUserPasswordMutationVariables>;
export const DeleteUserDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteUser"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"DeleteUserInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteUser"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}]}]}}]} as unknown as DocumentNode<DeleteUserMutation, DeleteUserMutationVariables>;
export const WelcomeDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Welcome"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"search"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"skip"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"take"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"myVehicles"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"search"},"value":{"kind":"Variable","name":{"kind":"Name","value":"search"}}},{"kind":"Argument","name":{"kind":"Name","value":"skip"},"value":{"kind":"Variable","name":{"kind":"Name","value":"skip"}}},{"kind":"Argument","name":{"kind":"Name","value":"take"},"value":{"kind":"Variable","name":{"kind":"Name","value":"take"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"licensePlate"}},{"kind":"Field","name":{"kind":"Name","value":"fuelType"}},{"kind":"Field","name":{"kind":"Name","value":"pictureUrl"}},{"kind":"Field","name":{"kind":"Name","value":"canEdit"}},{"kind":"Field","name":{"kind":"Name","value":"logAccess"}},{"kind":"Field","name":{"kind":"Name","value":"units"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"distance"}},{"kind":"Field","name":{"kind":"Name","value":"volume"}}]}},{"kind":"Field","name":{"kind":"Name","value":"owner"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}},{"kind":"Field","name":{"kind":"Name","value":"recurring"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"status"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"limit"}},{"kind":"Field","name":{"kind":"Name","value":"dueDate"}},{"kind":"Field","name":{"kind":"Name","value":"daysLeft"}},{"kind":"Field","name":{"kind":"Name","value":"distanceLeft"}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"summary"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"lastFillUpDate"}},{"kind":"Field","name":{"kind":"Name","value":"latestOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"averageConsumption"}},{"kind":"Field","name":{"kind":"Name","value":"currency"}},{"kind":"Field","name":{"kind":"Name","value":"thisMonthSpend"}},{"kind":"Field","name":{"kind":"Name","value":"lastMonthSpend"}},{"kind":"Field","name":{"kind":"Name","value":"fillUpCount"}},{"kind":"Field","name":{"kind":"Name","value":"expenseCount"}},{"kind":"Field","name":{"kind":"Name","value":"spendTrend"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"month"}},{"kind":"Field","name":{"kind":"Name","value":"amount"}}]}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"myVehicleCount"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"search"},"value":{"kind":"Variable","name":{"kind":"Name","value":"search"}}}]}]}}]} as unknown as DocumentNode<WelcomeQuery, WelcomeQueryVariables>;
export const VehicleDashboardDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"VehicleDashboard"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"vehicle"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"summary"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"lastFillUpDate"}},{"kind":"Field","name":{"kind":"Name","value":"latestOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"averageConsumption"}},{"kind":"Field","name":{"kind":"Name","value":"currency"}},{"kind":"Field","name":{"kind":"Name","value":"thisMonthSpend"}},{"kind":"Field","name":{"kind":"Name","value":"lastMonthSpend"}},{"kind":"Field","name":{"kind":"Name","value":"fillUpCount"}},{"kind":"Field","name":{"kind":"Name","value":"expenseCount"}},{"kind":"Field","name":{"kind":"Name","value":"spendTrend"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"month"}},{"kind":"Field","name":{"kind":"Name","value":"amount"}}]}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"vehicleCharts"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"metric"}},{"kind":"Field","name":{"kind":"Name","value":"grouping"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"range"}},{"kind":"Field","name":{"kind":"Name","value":"rangeFrom"}},{"kind":"Field","name":{"kind":"Name","value":"rangeTo"}},{"kind":"Field","name":{"kind":"Name","value":"stacked"}},{"kind":"Field","name":{"kind":"Name","value":"isShared"}},{"kind":"Field","name":{"kind":"Name","value":"canEdit"}},{"kind":"Field","name":{"kind":"Name","value":"createdBy"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}}]}}]} as unknown as DocumentNode<VehicleDashboardQuery, VehicleDashboardQueryVariables>;
export const ChartDataDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ChartData"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"config"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ChartConfigInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"vehicleChartData"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}},{"kind":"Argument","name":{"kind":"Name","value":"config"},"value":{"kind":"Variable","name":{"kind":"Name","value":"config"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"unit"}},{"kind":"Field","name":{"kind":"Name","value":"series"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"currency"}},{"kind":"Field","name":{"kind":"Name","value":"points"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"key"}},{"kind":"Field","name":{"kind":"Name","value":"value"}}]}}]}}]}}]}}]} as unknown as DocumentNode<ChartDataQuery, ChartDataQueryVariables>;
export const SaveChartDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SaveChart"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SaveChartInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"saveVehicleChart"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<SaveChartMutation, SaveChartMutationVariables>;
export const DeleteChartDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteChart"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteVehicleChart"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<DeleteChartMutation, DeleteChartMutationVariables>;
export const ExpensesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Expenses"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ExpenseSortField"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"direction"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SortDirection"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"skip"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"take"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withCategory"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withAmount"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withOdometer"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withNote"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withCreatedBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"expenses"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}},{"kind":"Argument","name":{"kind":"Name","value":"orderBy"},"value":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}}},{"kind":"Argument","name":{"kind":"Name","value":"direction"},"value":{"kind":"Variable","name":{"kind":"Name","value":"direction"}}},{"kind":"Argument","name":{"kind":"Name","value":"skip"},"value":{"kind":"Variable","name":{"kind":"Name","value":"skip"}}},{"kind":"Argument","name":{"kind":"Name","value":"take"},"value":{"kind":"Variable","name":{"kind":"Name","value":"take"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"date"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"canEdit"}},{"kind":"Field","name":{"kind":"Name","value":"canDelete"}},{"kind":"Field","name":{"kind":"Name","value":"category"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCategory"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"amount"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withAmount"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"currency"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withAmount"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"odometer"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withOdometer"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"note"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withNote"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"createdBy"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCreatedBy"}}}]}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"expenseCount"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}}]}]}}]} as unknown as DocumentNode<ExpensesQuery, ExpensesQueryVariables>;
export const ExpenseTrashDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ExpenseTrash"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ExpenseSortField"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"direction"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SortDirection"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"skip"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"take"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withCategory"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withAmount"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withDeletedAt"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withCreatedBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"expenseTrash"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"orderBy"},"value":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}}},{"kind":"Argument","name":{"kind":"Name","value":"direction"},"value":{"kind":"Variable","name":{"kind":"Name","value":"direction"}}},{"kind":"Argument","name":{"kind":"Name","value":"skip"},"value":{"kind":"Variable","name":{"kind":"Name","value":"skip"}}},{"kind":"Argument","name":{"kind":"Name","value":"take"},"value":{"kind":"Variable","name":{"kind":"Name","value":"take"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"date"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"vehicle"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}}]}},{"kind":"Field","name":{"kind":"Name","value":"category"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCategory"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"amount"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withAmount"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"currency"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withAmount"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"deletedAt"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withDeletedAt"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"createdBy"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCreatedBy"}}}]}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"expenseTrashCount"}},{"kind":"Field","name":{"kind":"Name","value":"expenseTrashDeletableCount"}}]}}]} as unknown as DocumentNode<ExpenseTrashQuery, ExpenseTrashQueryVariables>;
export const ExpenseDetailsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ExpenseDetails"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"expense"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"vehicleId"}},{"kind":"Field","name":{"kind":"Name","value":"date"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"category"}},{"kind":"Field","name":{"kind":"Name","value":"amount"}},{"kind":"Field","name":{"kind":"Name","value":"currency"}},{"kind":"Field","name":{"kind":"Name","value":"odometer"}},{"kind":"Field","name":{"kind":"Name","value":"note"}},{"kind":"Field","name":{"kind":"Name","value":"photos"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"url"}}]}}]}}]}}]} as unknown as DocumentNode<ExpenseDetailsQuery, ExpenseDetailsQueryVariables>;
export const ExpenseCategoriesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ExpenseCategories"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"expenseCategories"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}}]}]}}]} as unknown as DocumentNode<ExpenseCategoriesQuery, ExpenseCategoriesQueryVariables>;
export const AddExpenseDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"AddExpense"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"AddExpenseInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"addExpense"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<AddExpenseMutation, AddExpenseMutationVariables>;
export const UpdateExpenseDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateExpense"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UpdateExpenseInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateExpense"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<UpdateExpenseMutation, UpdateExpenseMutationVariables>;
export const DeleteExpenseDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteExpense"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteExpense"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<DeleteExpenseMutation, DeleteExpenseMutationVariables>;
export const RestoreExpenseDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"RestoreExpense"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"restoreExpense"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<RestoreExpenseMutation, RestoreExpenseMutationVariables>;
export const EmptyExpenseTrashDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"EmptyExpenseTrash"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"emptyExpenseTrash"}}]}}]} as unknown as DocumentNode<EmptyExpenseTrashMutation, EmptyExpenseTrashMutationVariables>;
export const HealthDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Health"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"health"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"status"}},{"kind":"Field","name":{"kind":"Name","value":"version"}},{"kind":"Field","name":{"kind":"Name","value":"databaseReachable"}}]}}]}}]} as unknown as DocumentNode<HealthQuery, HealthQueryVariables>;
export const ImportPreviewDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ImportPreview"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"token"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}},"type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"importPreview"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"token"},"value":{"kind":"Variable","name":{"kind":"Name","value":"token"}}},{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"fuelRows"}},{"kind":"Field","name":{"kind":"Name","value":"expenseRows"}},{"kind":"Field","name":{"kind":"Name","value":"recurringRows"}},{"kind":"Field","name":{"kind":"Name","value":"duplicateFuelRows"}},{"kind":"Field","name":{"kind":"Name","value":"duplicateExpenseRows"}},{"kind":"Field","name":{"kind":"Name","value":"duplicateRecurringRows"}},{"kind":"Field","name":{"kind":"Name","value":"firstDate"}},{"kind":"Field","name":{"kind":"Name","value":"lastDate"}},{"kind":"Field","name":{"kind":"Name","value":"categories"}},{"kind":"Field","name":{"kind":"Name","value":"sourceVehicle"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"licensePlate"}},{"kind":"Field","name":{"kind":"Name","value":"fuelType"}},{"kind":"Field","name":{"kind":"Name","value":"distanceUnit"}},{"kind":"Field","name":{"kind":"Name","value":"volumeUnit"}}]}},{"kind":"Field","name":{"kind":"Name","value":"issues"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"section"}},{"kind":"Field","name":{"kind":"Name","value":"row"}},{"kind":"Field","name":{"kind":"Name","value":"key"}},{"kind":"Field","name":{"kind":"Name","value":"args"}}]}}]}}]}}]} as unknown as DocumentNode<ImportPreviewQuery, ImportPreviewQueryVariables>;
export const ImportTargetsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"ImportTargets"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"myVehicles"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"take"},"value":{"kind":"IntValue","value":"200"}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"licensePlate"}},{"kind":"Field","name":{"kind":"Name","value":"canEdit"}},{"kind":"Field","name":{"kind":"Name","value":"logAccess"}},{"kind":"Field","name":{"kind":"Name","value":"units"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"distance"}},{"kind":"Field","name":{"kind":"Name","value":"volume"}}]}}]}}]}}]} as unknown as DocumentNode<ImportTargetsQuery, ImportTargetsQueryVariables>;
export const ConfirmImportDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"ConfirmImport"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ConfirmImportInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"confirmImport"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"vehicleId"}},{"kind":"Field","name":{"kind":"Name","value":"fuelImported"}},{"kind":"Field","name":{"kind":"Name","value":"expensesImported"}},{"kind":"Field","name":{"kind":"Name","value":"recurringImported"}},{"kind":"Field","name":{"kind":"Name","value":"fuelSkippedDuplicates"}},{"kind":"Field","name":{"kind":"Name","value":"expensesSkippedDuplicates"}},{"kind":"Field","name":{"kind":"Name","value":"recurringSkippedDuplicates"}},{"kind":"Field","name":{"kind":"Name","value":"errors"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"section"}},{"kind":"Field","name":{"kind":"Name","value":"row"}},{"kind":"Field","name":{"kind":"Name","value":"key"}},{"kind":"Field","name":{"kind":"Name","value":"args"}}]}}]}}]}}]} as unknown as DocumentNode<ConfirmImportMutation, ConfirmImportMutationVariables>;
export const RecurringExpensesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"RecurringExpenses"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"vehicle"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"recurring"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"RecurringFields"}}]}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"RecurringFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"RecurringExpenseInfo"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"category"}},{"kind":"Field","name":{"kind":"Name","value":"note"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"intervalMonths"}},{"kind":"Field","name":{"kind":"Name","value":"intervalDistance"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneDate"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"warnDays"}},{"kind":"Field","name":{"kind":"Name","value":"warnDistance"}},{"kind":"Field","name":{"kind":"Name","value":"status"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"limit"}},{"kind":"Field","name":{"kind":"Name","value":"dueDate"}},{"kind":"Field","name":{"kind":"Name","value":"dueOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"daysLeft"}},{"kind":"Field","name":{"kind":"Name","value":"distanceLeft"}}]}}]}}]} as unknown as DocumentNode<RecurringExpensesQuery, RecurringExpensesQueryVariables>;
export const AddRecurringExpenseDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"AddRecurringExpense"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"AddRecurringExpenseInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"addRecurringExpense"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"RecurringFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"RecurringFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"RecurringExpenseInfo"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"category"}},{"kind":"Field","name":{"kind":"Name","value":"note"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"intervalMonths"}},{"kind":"Field","name":{"kind":"Name","value":"intervalDistance"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneDate"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"warnDays"}},{"kind":"Field","name":{"kind":"Name","value":"warnDistance"}},{"kind":"Field","name":{"kind":"Name","value":"status"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"limit"}},{"kind":"Field","name":{"kind":"Name","value":"dueDate"}},{"kind":"Field","name":{"kind":"Name","value":"dueOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"daysLeft"}},{"kind":"Field","name":{"kind":"Name","value":"distanceLeft"}}]}}]}}]} as unknown as DocumentNode<AddRecurringExpenseMutation, AddRecurringExpenseMutationVariables>;
export const UpdateRecurringExpenseDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateRecurringExpense"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UpdateRecurringExpenseInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateRecurringExpense"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"RecurringFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"RecurringFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"RecurringExpenseInfo"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"category"}},{"kind":"Field","name":{"kind":"Name","value":"note"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"intervalMonths"}},{"kind":"Field","name":{"kind":"Name","value":"intervalDistance"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneDate"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"warnDays"}},{"kind":"Field","name":{"kind":"Name","value":"warnDistance"}},{"kind":"Field","name":{"kind":"Name","value":"status"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"limit"}},{"kind":"Field","name":{"kind":"Name","value":"dueDate"}},{"kind":"Field","name":{"kind":"Name","value":"dueOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"daysLeft"}},{"kind":"Field","name":{"kind":"Name","value":"distanceLeft"}}]}}]}}]} as unknown as DocumentNode<UpdateRecurringExpenseMutation, UpdateRecurringExpenseMutationVariables>;
export const DeleteRecurringExpenseDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteRecurringExpense"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteRecurringExpense"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}]}]}}]} as unknown as DocumentNode<DeleteRecurringExpenseMutation, DeleteRecurringExpenseMutationVariables>;
export const MarkRecurringExpenseDoneDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"MarkRecurringExpenseDone"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"MarkRecurringExpenseDoneInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"markRecurringExpenseDone"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"FragmentSpread","name":{"kind":"Name","value":"RecurringFields"}}]}}]}},{"kind":"FragmentDefinition","name":{"kind":"Name","value":"RecurringFields"},"typeCondition":{"kind":"NamedType","name":{"kind":"Name","value":"RecurringExpenseInfo"}},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"title"}},{"kind":"Field","name":{"kind":"Name","value":"category"}},{"kind":"Field","name":{"kind":"Name","value":"note"}},{"kind":"Field","name":{"kind":"Name","value":"kind"}},{"kind":"Field","name":{"kind":"Name","value":"intervalMonths"}},{"kind":"Field","name":{"kind":"Name","value":"intervalDistance"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneDate"}},{"kind":"Field","name":{"kind":"Name","value":"lastDoneOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"warnDays"}},{"kind":"Field","name":{"kind":"Name","value":"warnDistance"}},{"kind":"Field","name":{"kind":"Name","value":"status"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"state"}},{"kind":"Field","name":{"kind":"Name","value":"limit"}},{"kind":"Field","name":{"kind":"Name","value":"dueDate"}},{"kind":"Field","name":{"kind":"Name","value":"dueOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"daysLeft"}},{"kind":"Field","name":{"kind":"Name","value":"distanceLeft"}}]}}]}}]} as unknown as DocumentNode<MarkRecurringExpenseDoneMutation, MarkRecurringExpenseDoneMutationVariables>;
export const RefuelingsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Refuelings"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"RefuelingSortField"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"direction"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SortDirection"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"skip"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"take"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withVolume"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withCost"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withPrice"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withConsumption"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withOdometer"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withFullTank"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withNote"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withCreatedBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"refuelings"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}},{"kind":"Argument","name":{"kind":"Name","value":"orderBy"},"value":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}}},{"kind":"Argument","name":{"kind":"Name","value":"direction"},"value":{"kind":"Variable","name":{"kind":"Name","value":"direction"}}},{"kind":"Argument","name":{"kind":"Name","value":"skip"},"value":{"kind":"Variable","name":{"kind":"Name","value":"skip"}}},{"kind":"Argument","name":{"kind":"Name","value":"take"},"value":{"kind":"Variable","name":{"kind":"Name","value":"take"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"date"}},{"kind":"Field","name":{"kind":"Name","value":"canEdit"}},{"kind":"Field","name":{"kind":"Name","value":"canDelete"}},{"kind":"Field","name":{"kind":"Name","value":"volume"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withVolume"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"totalCost"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCost"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"currency"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCost"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"pricePerUnit"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withPrice"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"consumption"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withConsumption"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"odometer"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withOdometer"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"isFullTank"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withFullTank"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"note"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withNote"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"createdBy"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCreatedBy"}}}]}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"refuelingCount"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}}]}]}}]} as unknown as DocumentNode<RefuelingsQuery, RefuelingsQueryVariables>;
export const RefuelingTrashDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"RefuelingTrash"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"RefuelingSortField"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"direction"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SortDirection"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"skip"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"take"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withVolume"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withCost"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withOdometer"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withDeletedAt"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withCreatedBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"refuelingTrash"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"orderBy"},"value":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}}},{"kind":"Argument","name":{"kind":"Name","value":"direction"},"value":{"kind":"Variable","name":{"kind":"Name","value":"direction"}}},{"kind":"Argument","name":{"kind":"Name","value":"skip"},"value":{"kind":"Variable","name":{"kind":"Name","value":"skip"}}},{"kind":"Argument","name":{"kind":"Name","value":"take"},"value":{"kind":"Variable","name":{"kind":"Name","value":"take"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"date"}},{"kind":"Field","name":{"kind":"Name","value":"vehicle"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"units"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"distance"}},{"kind":"Field","name":{"kind":"Name","value":"volume"}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"volume"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withVolume"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"totalCost"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCost"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"currency"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCost"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"odometer"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withOdometer"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"deletedAt"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withDeletedAt"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"createdBy"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withCreatedBy"}}}]}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"refuelingTrashCount"}},{"kind":"Field","name":{"kind":"Name","value":"refuelingTrashDeletableCount"}}]}}]} as unknown as DocumentNode<RefuelingTrashQuery, RefuelingTrashQueryVariables>;
export const RefuelingDetailsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"RefuelingDetails"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"refueling"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"vehicleId"}},{"kind":"Field","name":{"kind":"Name","value":"date"}},{"kind":"Field","name":{"kind":"Name","value":"volume"}},{"kind":"Field","name":{"kind":"Name","value":"totalCost"}},{"kind":"Field","name":{"kind":"Name","value":"currency"}},{"kind":"Field","name":{"kind":"Name","value":"odometer"}},{"kind":"Field","name":{"kind":"Name","value":"isFullTank"}},{"kind":"Field","name":{"kind":"Name","value":"note"}},{"kind":"Field","name":{"kind":"Name","value":"photos"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"url"}}]}}]}}]}}]} as unknown as DocumentNode<RefuelingDetailsQuery, RefuelingDetailsQueryVariables>;
export const LogDefaultsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"LogDefaults"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"logDefaults"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"lastOdometer"}},{"kind":"Field","name":{"kind":"Name","value":"lastDate"}},{"kind":"Field","name":{"kind":"Name","value":"currency"}}]}}]}}]} as unknown as DocumentNode<LogDefaultsQuery, LogDefaultsQueryVariables>;
export const LogRefuelingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"LogRefueling"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"LogRefuelingInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"logRefueling"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<LogRefuelingMutation, LogRefuelingMutationVariables>;
export const UpdateRefuelingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateRefueling"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UpdateRefuelingInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateRefueling"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<UpdateRefuelingMutation, UpdateRefuelingMutationVariables>;
export const DeleteRefuelingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteRefueling"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteRefueling"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<DeleteRefuelingMutation, DeleteRefuelingMutationVariables>;
export const RestoreRefuelingDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"RestoreRefueling"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"restoreRefueling"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<RestoreRefuelingMutation, RestoreRefuelingMutationVariables>;
export const EmptyRefuelingTrashDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"EmptyRefuelingTrash"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"emptyRefuelingTrash"}}]}}]} as unknown as DocumentNode<EmptyRefuelingTrashMutation, EmptyRefuelingTrashMutationVariables>;
export const SessionDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Session"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"session"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"mode"}},{"kind":"Field","name":{"kind":"Name","value":"user"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"email"}},{"kind":"Field","name":{"kind":"Name","value":"isAdmin"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"notices"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"code"}},{"kind":"Field","name":{"kind":"Name","value":"severity"}},{"kind":"Field","name":{"kind":"Name","value":"message"}}]}}]}}]} as unknown as DocumentNode<SessionQuery, SessionQueryVariables>;
export const LoginDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"Login"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"LoginInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"login"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<LoginMutation, LoginMutationVariables>;
export const LogoutDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"Logout"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"logout"}}]}}]} as unknown as DocumentNode<LogoutMutation, LogoutMutationVariables>;
export const ChangePasswordDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"ChangePassword"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ChangePasswordInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"changePassword"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}]}]}}]} as unknown as DocumentNode<ChangePasswordMutation, ChangePasswordMutationVariables>;
export const RequestPasswordResetDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"RequestPasswordReset"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"email"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"String"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"requestPasswordReset"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"email"},"value":{"kind":"Variable","name":{"kind":"Name","value":"email"}}}]}]}}]} as unknown as DocumentNode<RequestPasswordResetMutation, RequestPasswordResetMutationVariables>;
export const ResetPasswordDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"ResetPassword"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"ResetPasswordInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"resetPassword"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}]}]}}]} as unknown as DocumentNode<ResetPasswordMutation, ResetPasswordMutationVariables>;
export const LogAccessDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"LogAccess"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"vehicleLogAccess"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"level"}},{"kind":"Field","name":{"kind":"Name","value":"user"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}},{"kind":"Field","name":{"kind":"Name","value":"shareCandidates"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"vehicleId"},"value":{"kind":"Variable","name":{"kind":"Name","value":"vehicleId"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}}]} as unknown as DocumentNode<LogAccessQuery, LogAccessQueryVariables>;
export const SetVehicleLogAccessDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"SetVehicleLogAccess"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SetLogAccessInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"setVehicleLogAccess"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}]}]}}]} as unknown as DocumentNode<SetVehicleLogAccessMutation, SetVehicleLogAccessMutationVariables>;
export const VehiclesDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Vehicles"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"VehicleSortField"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"direction"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SortDirection"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"skip"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"take"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withLicensePlate"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withFuelType"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withOwner"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withRefuelings"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"vehicles"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"orderBy"},"value":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}}},{"kind":"Argument","name":{"kind":"Name","value":"direction"},"value":{"kind":"Variable","name":{"kind":"Name","value":"direction"}}},{"kind":"Argument","name":{"kind":"Name","value":"skip"},"value":{"kind":"Variable","name":{"kind":"Name","value":"skip"}}},{"kind":"Argument","name":{"kind":"Name","value":"take"},"value":{"kind":"Variable","name":{"kind":"Name","value":"take"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"canEdit"}},{"kind":"Field","name":{"kind":"Name","value":"pictureUrl"}},{"kind":"Field","name":{"kind":"Name","value":"licensePlate"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withLicensePlate"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"fuelType"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withFuelType"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"owner"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withOwner"}}}]}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}},{"kind":"Field","name":{"kind":"Name","value":"refuelingCount"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withRefuelings"}}}]}]}]}},{"kind":"Field","name":{"kind":"Name","value":"vehicleCount"}}]}}]} as unknown as DocumentNode<VehiclesQuery, VehiclesQueryVariables>;
export const TrashDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"Trash"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"VehicleSortField"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"direction"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"SortDirection"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"skip"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"take"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Int"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withLicensePlate"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withFuelType"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withOwner"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}},{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"withDeletedAt"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"Boolean"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"trash"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"orderBy"},"value":{"kind":"Variable","name":{"kind":"Name","value":"orderBy"}}},{"kind":"Argument","name":{"kind":"Name","value":"direction"},"value":{"kind":"Variable","name":{"kind":"Name","value":"direction"}}},{"kind":"Argument","name":{"kind":"Name","value":"skip"},"value":{"kind":"Variable","name":{"kind":"Name","value":"skip"}}},{"kind":"Argument","name":{"kind":"Name","value":"take"},"value":{"kind":"Variable","name":{"kind":"Name","value":"take"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"pictureUrl"}},{"kind":"Field","name":{"kind":"Name","value":"licensePlate"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withLicensePlate"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"fuelType"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withFuelType"}}}]}]},{"kind":"Field","name":{"kind":"Name","value":"owner"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withOwner"}}}]}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}},{"kind":"Field","name":{"kind":"Name","value":"deletedAt"},"directives":[{"kind":"Directive","name":{"kind":"Name","value":"include"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"if"},"value":{"kind":"Variable","name":{"kind":"Name","value":"withDeletedAt"}}}]}]}]}},{"kind":"Field","name":{"kind":"Name","value":"trashCount"}},{"kind":"Field","name":{"kind":"Name","value":"trashDeletableCount"}}]}}]} as unknown as DocumentNode<TrashQuery, TrashQueryVariables>;
export const VehicleDetailsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"VehicleDetails"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"vehicle"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"name"}},{"kind":"Field","name":{"kind":"Name","value":"licensePlate"}},{"kind":"Field","name":{"kind":"Name","value":"fuelType"}},{"kind":"Field","name":{"kind":"Name","value":"pictureUrl"}},{"kind":"Field","name":{"kind":"Name","value":"canEdit"}},{"kind":"Field","name":{"kind":"Name","value":"logAccess"}},{"kind":"Field","name":{"kind":"Name","value":"refuelingCount"}},{"kind":"Field","name":{"kind":"Name","value":"units"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"distance"}},{"kind":"Field","name":{"kind":"Name","value":"volume"}}]}},{"kind":"Field","name":{"kind":"Name","value":"owner"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}},{"kind":"Field","name":{"kind":"Name","value":"displayName"}},{"kind":"Field","name":{"kind":"Name","value":"avatarUrl"}}]}}]}}]}}]} as unknown as DocumentNode<VehicleDetailsQuery, VehicleDetailsQueryVariables>;
export const VehicleDefaultsDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"query","name":{"kind":"Name","value":"VehicleDefaults"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"vehicleDefaults"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"distanceUnit"}},{"kind":"Field","name":{"kind":"Name","value":"volumeUnit"}},{"kind":"Field","name":{"kind":"Name","value":"currency"}}]}}]}}]} as unknown as DocumentNode<VehicleDefaultsQuery, VehicleDefaultsQueryVariables>;
export const AddVehicleDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"AddVehicle"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"AddVehicleInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"addVehicle"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<AddVehicleMutation, AddVehicleMutationVariables>;
export const UpdateVehicleDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"UpdateVehicle"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"input"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UpdateVehicleInput"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"updateVehicle"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"input"},"value":{"kind":"Variable","name":{"kind":"Name","value":"input"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<UpdateVehicleMutation, UpdateVehicleMutationVariables>;
export const DeleteVehicleDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"DeleteVehicle"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"deleteVehicle"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<DeleteVehicleMutation, DeleteVehicleMutationVariables>;
export const RestoreVehicleDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"RestoreVehicle"},"variableDefinitions":[{"kind":"VariableDefinition","variable":{"kind":"Variable","name":{"kind":"Name","value":"id"}},"type":{"kind":"NonNullType","type":{"kind":"NamedType","name":{"kind":"Name","value":"UUID"}}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"restoreVehicle"},"arguments":[{"kind":"Argument","name":{"kind":"Name","value":"id"},"value":{"kind":"Variable","name":{"kind":"Name","value":"id"}}}],"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"id"}}]}}]}}]} as unknown as DocumentNode<RestoreVehicleMutation, RestoreVehicleMutationVariables>;
export const EmptyTrashDocument = {"kind":"Document","definitions":[{"kind":"OperationDefinition","operation":"mutation","name":{"kind":"Name","value":"EmptyTrash"},"selectionSet":{"kind":"SelectionSet","selections":[{"kind":"Field","name":{"kind":"Name","value":"emptyTrash"}}]}}]} as unknown as DocumentNode<EmptyTrashMutation, EmptyTrashMutationVariables>;