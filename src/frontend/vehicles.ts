import { gql, type TypedDocumentNode } from '@apollo/client'

export type FuelType = 'PETROL' | 'DIESEL' | 'LPG'

export const FUEL_TYPES: { value: FuelType; label: string }[] = [
  { value: 'PETROL', label: 'Petrol' },
  { value: 'DIESEL', label: 'Diesel' },
  { value: 'LPG', label: 'LPG' },
]

export const fuelLabel = (f: FuelType) => FUEL_TYPES.find((x) => x.value === f)?.label ?? f

export interface Vehicle {
  id: string
  name: string
  licensePlate: string | null
  fuelType: FuelType
  ownerName: string | null
  canEdit: boolean
  refuelings: { id: string; odometerKm: number }[]
}

export interface TrashedVehicle {
  id: string
  name: string
  licensePlate: string | null
  fuelType: FuelType
  ownerName: string | null
  deletedAt: string
}

export interface VehicleInput {
  name: string
  licensePlate: string | null
  fuelType: FuelType
}

export const VEHICLES_QUERY: TypedDocumentNode<{ vehicles: Vehicle[] }> = gql`
  query Vehicles {
    vehicles {
      id
      name
      licensePlate
      fuelType
      ownerName
      canEdit
      refuelings {
        id
        odometerKm
      }
    }
  }
`

export const TRASH_QUERY: TypedDocumentNode<{ trash: TrashedVehicle[] }> = gql`
  query Trash {
    trash {
      id
      name
      licensePlate
      fuelType
      ownerName
      deletedAt
    }
  }
`

export const ADD_VEHICLE_MUTATION: TypedDocumentNode<{ addVehicle: { id: string } }, { input: VehicleInput }> = gql`
  mutation AddVehicle($input: AddVehicleInput!) {
    addVehicle(input: $input) {
      id
    }
  }
`

export const UPDATE_VEHICLE_MUTATION: TypedDocumentNode<{ updateVehicle: { id: string } }, { input: VehicleInput & { id: string } }> = gql`
  mutation UpdateVehicle($input: UpdateVehicleInput!) {
    updateVehicle(input: $input) {
      id
    }
  }
`

export const DELETE_VEHICLE_MUTATION: TypedDocumentNode<{ deleteVehicle: { id: string } }, { id: string }> = gql`
  mutation DeleteVehicle($id: UUID!) {
    deleteVehicle(id: $id) {
      id
    }
  }
`

export const RESTORE_VEHICLE_MUTATION: TypedDocumentNode<{ restoreVehicle: { id: string } }, { id: string }> = gql`
  mutation RestoreVehicle($id: UUID!) {
    restoreVehicle(id: $id) {
      id
    }
  }
`

export const EMPTY_TRASH_MUTATION: TypedDocumentNode<{ emptyTrash: number }> = gql`
  mutation EmptyTrash {
    emptyTrash
  }
`
