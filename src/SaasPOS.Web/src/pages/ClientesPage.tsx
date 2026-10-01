import { useState, useEffect, useCallback } from 'react'
import { api } from '../lib/api'
import { Modal } from '../components/ui/Modal'
import { Button } from '../components/ui/Button'
import { ResponsiveTable } from '../components/ui/ResponsiveTable'

// --- Types ---
interface Cliente {
  id: string
  identificacion: string
  nombre: string
  correo: string
  direccion: string
  telefono: string
  esConsumidorFinal: boolean
}

interface FormErrors {
  [key: string]: string
}

// --- ClientesPage ---
export function ClientesPage() {
  const [clientes, setClientes] = useState<Cliente[]>([])
  const [loading, setLoading] = useState(true)
  const [searchId, setSearchId] = useState('')
  const [searchResult, setSearchResult] = useState<'idle' | 'found' | 'not-found'>('idle')

  // Modal state
  const [modalOpen, setModalOpen] = useState(false)
  const [editingClient, setEditingClient] = useState<Cliente | null>(null)
  const [formDirty, setFormDirty] = useState(false)
  const [saving, setSaving] = useState(false)

  // Form state
  const [form, setForm] = useState({
    identificacion: '',
    nombre: '',
    correo: '',
    direccion: '',
    telefono: '',
    esConsumidorFinal: false,
  })
  const [errors, setErrors] = useState<FormErrors>({})

  // --- Load data ---
  const loadClientes = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api.get<{ items: Cliente[]; total: number }>('/api/tenants/clientes')
      setClientes(data.items ?? [])
    } catch {
      setClientes([])
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadClientes() }, [loadClientes])

  // --- Search by identification (Req 12.4) ---
  const handleSearch = async () => {
    if (!searchId.trim()) {
      setSearchResult('idle')
      return
    }
    try {
      const found = await api.get<Cliente>(`/api/tenants/clientes/${encodeURIComponent(searchId.trim())}`)
      if (found) {
        // Filter to show only the found client
        setClientes([found])
        setSearchResult('found')
      }
    } catch {
      // No results - offer to create with searched data pre-filled (Req 12.4)
      setSearchResult('not-found')
    }
  }

  const handleSearchKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleSearch()
  }

  const clearSearch = async () => {
    setSearchId('')
    setSearchResult('idle')
    await loadClientes()
  }

  // --- Req 12.4: Create with pre-filled search term ---
  const createFromSearch = () => {
    setEditingClient(null)
    setForm({
      identificacion: searchId.trim(),
      nombre: '',
      correo: '',
      direccion: '',
      telefono: '',
      esConsumidorFinal: false,
    })
    setErrors({})
    setFormDirty(false)
    setModalOpen(true)
  }

  // --- Validation (Req 21.13) ---
  const validate = (): boolean => {
    const newErrors: FormErrors = {}
    if (!form.identificacion.trim()) newErrors.identificacion = 'La identificación es requerida'
    if (!form.nombre.trim()) newErrors.nombre = 'El nombre es requerido'

    // Req 12.2: when EsConsumidorFinal=FALSE, correo/direccion/telefono may still be optional
    // But we validate format if provided
    if (!form.esConsumidorFinal) {
      // No extra mandatory fields beyond identificacion and nombre based on Req 12.2 wording
      // Req 12.2 says "when EsConsumidorFinal=TRUE, make Correo, Dirección and Teléfono optional"
      // implying they are required when FALSE
      if (!form.correo.trim()) newErrors.correo = 'El correo es requerido cuando no es consumidor final'
      if (!form.direccion.trim()) newErrors.direccion = 'La dirección es requerida cuando no es consumidor final'
      if (!form.telefono.trim()) newErrors.telefono = 'El teléfono es requerido cuando no es consumidor final'
    }

    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }

  // --- Modal actions ---
  const openCreate = () => {
    setEditingClient(null)
    setForm({
      identificacion: '',
      nombre: '',
      correo: '',
      direccion: '',
      telefono: '',
      esConsumidorFinal: false,
    })
    setErrors({})
    setFormDirty(false)
    setModalOpen(true)
  }

  const openEdit = (client: Cliente) => {
    setEditingClient(client)
    setForm({
      identificacion: client.identificacion,
      nombre: client.nombre,
      correo: client.correo ?? '',
      direccion: client.direccion ?? '',
      telefono: client.telefono ?? '',
      esConsumidorFinal: client.esConsumidorFinal,
    })
    setErrors({})
    setFormDirty(false)
    setModalOpen(true)
  }

  const handleSave = async () => {
    if (!validate()) return
    setSaving(true)
    try {
      if (editingClient) {
        await api.put(`/api/tenants/clientes/${editingClient.id}`, form)
      } else {
        await api.post('/api/tenants/clientes', form)
      }
      setModalOpen(false)
      setSearchResult('idle')
      setSearchId('')
      await loadClientes()
    } catch {
      // handled silently
    } finally {
      setSaving(false)
    }
  }

  // --- Field update helper ---
  const updateField = (field: string, value: unknown) => {
    setForm(prev => ({ ...prev, [field]: value }))
    setFormDirty(true)
    if (errors[field]) setErrors(prev => { const n = { ...prev }; delete n[field]; return n })
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Clientes</h1>
        <Button variant="confirm" onClick={openCreate}>Nuevo Cliente</Button>
      </div>

      {/* Search by identification */}
      <div className="flex flex-col sm:flex-row gap-3 mb-4">
        <div className="flex flex-1 gap-2">
          <input
            type="text"
            placeholder="Buscar por identificación..."
            value={searchId}
            onChange={e => { setSearchId(e.target.value); if (!e.target.value) clearSearch() }}
            onKeyDown={handleSearchKeyDown}
            className="flex-1 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
          />
          <Button variant="neutral" onClick={handleSearch}>Buscar</Button>
          {searchResult !== 'idle' && (
            <Button variant="neutral" onClick={clearSearch}>Limpiar</Button>
          )}
        </div>
      </div>

      {/* Req 12.4: No results message with create option */}
      {searchResult === 'not-found' && (
        <div className="mb-4 p-4 rounded-lg bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-800">
          <p className="text-sm text-yellow-800 dark:text-yellow-200 mb-2">
            No se encontró un cliente con identificación "<strong>{searchId}</strong>".
          </p>
          <Button variant="confirm" onClick={createFromSearch} className="text-xs">
            Crear cliente con esta identificación
          </Button>
        </div>
      )}

      {/* Client table */}
      {loading ? (
        <p className="text-gray-500 dark:text-gray-400">Cargando...</p>
      ) : (
        <ResponsiveTable className="rounded-lg border border-gray-200 dark:border-gray-700">
          <table className="w-full text-sm text-left">
            <thead className="bg-gray-50 dark:bg-dark-surface text-gray-600 dark:text-gray-300">
              <tr>
                <th className="px-4 py-3 sticky-col">Identificación</th>
                <th className="px-4 py-3">Nombre</th>
                <th className="px-4 py-3">Correo</th>
                <th className="px-4 py-3">Teléfono</th>
                <th className="px-4 py-3 text-center">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200 dark:divide-gray-700">
              {clientes.map(c => (
                <tr key={c.id} className="bg-white dark:bg-dark-bg hover:bg-gray-50 dark:hover:bg-dark-surface/50">
                  <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-medium sticky-col">{c.identificacion}</td>
                  <td className="px-4 py-3 text-gray-900 dark:text-dark-text">{c.nombre}</td>
                  <td className="px-4 py-3 text-gray-600 dark:text-gray-400">{c.correo || '-'}</td>
                  <td className="px-4 py-3 text-gray-600 dark:text-gray-400">{c.telefono || '-'}</td>
                  <td className="px-4 py-3 text-center">
                    <Button variant="edit" onClick={() => openEdit(c)} className="!px-3 !py-1 text-xs">Editar</Button>
                  </td>
                </tr>
              ))}
              {clientes.length === 0 && (
                <tr><td colSpan={5} className="px-4 py-8 text-center text-gray-500 dark:text-gray-400">No se encontraron clientes</td></tr>
              )}
            </tbody>
          </table>
        </ResponsiveTable>
      )}

      {/* Create/Edit Client Modal */}
      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editingClient ? 'Editar Cliente' : 'Nuevo Cliente'}
        warnOnClose={formDirty}
        size="md"
      >
        <div className="space-y-4">
          {/* Identificación */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Identificación *</label>
            <input
              type="text"
              value={form.identificacion}
              onChange={e => updateField('identificacion', e.target.value)}
              disabled={!!editingClient}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text disabled:opacity-50"
            />
            {errors.identificacion && <p className="mt-1 text-xs text-action-danger">{errors.identificacion}</p>}
          </div>

          {/* Nombre */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Nombre *</label>
            <input
              type="text"
              value={form.nombre}
              onChange={e => updateField('nombre', e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            />
            {errors.nombre && <p className="mt-1 text-xs text-action-danger">{errors.nombre}</p>}
          </div>

          {/* Es Consumidor Final (Req 12.2) */}
          <div>
            <label className="flex items-center gap-2 text-sm font-medium text-gray-700 dark:text-gray-300">
              <input
                type="checkbox"
                checked={form.esConsumidorFinal}
                onChange={e => {
                  updateField('esConsumidorFinal', e.target.checked)
                  // Clear validation errors for optional fields when toggling to consumidor final
                  if (e.target.checked) {
                    setErrors(prev => {
                      const n = { ...prev }
                      delete n.correo
                      delete n.direccion
                      delete n.telefono
                      return n
                    })
                  }
                }}
                className="rounded border-gray-300"
              />
              Es Consumidor Final
            </label>
            <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
              {form.esConsumidorFinal
                ? 'Correo, Dirección y Teléfono son opcionales.'
                : 'Correo, Dirección y Teléfono son requeridos.'}
            </p>
          </div>

          {/* Correo */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Correo {!form.esConsumidorFinal && '*'}
            </label>
            <input
              type="email"
              value={form.correo}
              onChange={e => updateField('correo', e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            />
            {errors.correo && <p className="mt-1 text-xs text-action-danger">{errors.correo}</p>}
          </div>

          {/* Dirección */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Dirección {!form.esConsumidorFinal && '*'}
            </label>
            <input
              type="text"
              value={form.direccion}
              onChange={e => updateField('direccion', e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            />
            {errors.direccion && <p className="mt-1 text-xs text-action-danger">{errors.direccion}</p>}
          </div>

          {/* Teléfono */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Teléfono {!form.esConsumidorFinal && '*'}
            </label>
            <input
              type="tel"
              value={form.telefono}
              onChange={e => updateField('telefono', e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            />
            {errors.telefono && <p className="mt-1 text-xs text-action-danger">{errors.telefono}</p>}
          </div>

          {/* Action buttons */}
          <div className="flex justify-end gap-3 pt-4 border-t border-gray-200 dark:border-gray-700">
            <Button variant="neutral" onClick={() => setModalOpen(false)} disabled={saving}>Cancelar</Button>
            <Button variant="confirm" onClick={handleSave} loading={saving}>
              {editingClient ? 'Guardar Cambios' : 'Crear Cliente'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  )
}
