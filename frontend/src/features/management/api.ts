import { getJson, postJson, putJson } from '../../lib/api/httpClient'
import type { CatalogItem, CatalogKind, Setting, StatusFilter } from './types'

const MANAGER = '/api/manager'

export function listCatalogItems(kind: CatalogKind, status: StatusFilter, signal?: AbortSignal): Promise<CatalogItem[]> {
  return getJson<CatalogItem[]>(`${MANAGER}/catalog/${kind}?status=${status}`, { signal })
}

export function createCatalogItem(
  kind: CatalogKind,
  input: { name: string; displayOrder: number; initialPrice: number },
): Promise<CatalogItem> {
  return postJson<CatalogItem>(`${MANAGER}/catalog/${kind}`, input)
}

export function updateCatalogItem(
  kind: CatalogKind,
  id: number,
  input: { name: string; displayOrder: number; version: number },
): Promise<CatalogItem> {
  return putJson<CatalogItem>(`${MANAGER}/catalog/${kind}/${id}`, input)
}

export function setCatalogItemActive(kind: CatalogKind, id: number, active: boolean, version: number): Promise<CatalogItem> {
  return postJson<CatalogItem>(`${MANAGER}/catalog/${kind}/${id}/${active ? 'activate' : 'deactivate'}`, { version })
}

export function listSettings(signal?: AbortSignal): Promise<Setting[]> {
  return getJson<Setting[]>(`${MANAGER}/settings`, { signal })
}