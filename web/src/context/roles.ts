export type Role = 'Cliente' | 'Recepcion' | 'Direccion' | 'Operaciones'

export const ROLE_LABEL: Record<Role, string> = {
  Cliente: 'Cliente',
  Recepcion: 'Recepción',
  Direccion: 'Dirección',
  Operaciones: 'Operaciones',
}

// Who signs the actions of each role (there is no authentication: the role is just a selector).
export const ROLE_ACTOR: Record<Exclude<Role, 'Cliente'>, string> = {
  Recepcion: 'Recepción Nodo',
  Direccion: 'Lucía Paz',
  Operaciones: 'Operaciones Nodo',
}
