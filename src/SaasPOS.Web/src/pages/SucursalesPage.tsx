import { useState, useEffect } from 'react'
import { api } from '../lib/api'
import { Button } from '../components/ui/Button'
import { Modal } from '../components/ui/Modal'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { ResponsiveTable } from '../components/ui/ResponsiveTable'

interface Sucursal {
  id: string
  nombre: string
  direccion: string
  telefono: string
  serieFacturacion: string
}

interface FormErrors {
  nombre?: string
}

/**
 * Sucursales page.
 * CRUD for branches with modal forms (Req 21.9), inline validation (Req 21.13),
 * and confirmation before delete (Req 21.15, 5.5).
 */
export function SucursalesPage() {
  const [sucursales, setSucursales] = useState<Sucursal[]>([])
  const [loading, setLoading] = useState(true)

  // Modal state
  const [showModal, setShowModal] = useState(false)
  const [editingSucursal, setEditingSucursal] = useState<Sucursal | null>(null)
  const [saving, setSaving] = useState(false)

  // Form fields
  const [nombre, setNombre] = useState('')
  const [direccion, setDireccion] = useState('')
  const [telefono, setTelefono] = useState('')
  const [serieFacturacion, setSerieFacturacion] = useState('')
  const [errors, setErrors] = useState<FormErrors>({})

  // Delete confirmation
  const [deleteTarget, setDeleteTarget] = useState<Sucursal | null>(null)
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    fetchSucursales()
  }, [])

  async function fetchSucursales() {
    setLoading(true)
    try {
      const data = await api.get<Sucursal[]>('/api/tenants/sucursales')
      setSucursales(data)
    } catch {
      setSucursales([])
    } finally {
      setLoading(false)
    }
  }

  function openCreateModal() {
    setEditingSucursal(null)
    setNombre('')
    setDireccion('')
    setTelefono('')
    setSerieFacturacion('')
    setErrors({})
    setShowModal(true)
  }

  function openEditModal(sucursal: Sucursal) {
    setEditingSucursal(sucursal)
    setNombre(sucursal.nombre)
    setDireccion(sucursal.direccion)
    setTelefono(sucursal.telefono)
    setSerieFacturacion(sucursal.serieFacturacion)
    setErrors({})
    setShowModal(true)
  }

  function validateForm(): boolean {
    const newErrors: FormErrors = {}

    if (!nombre.trim()) {
      newErrors.nombre = 'El nombre es requerido'
    }

    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }

  async function handleSubmit() {
    if (!validateForm()) return

    setSaving(true)
    try {
      const body = {
        nombre: nombre.trim(),
        direccion: direccion.trim(),
        telefono: telefono.trim(),
        serieFacturacion: serieFacturacion.trim(),
      }

      if (editingSucursal) {
        await api.put(`/api/tenants/sucursales/${editingSucursal.id}`, body)
      } else {
        await api.post('/api/tenants/sucursales', body)
      }

      setShowModal(false)
      await fetchSucursales()
    } catch {
      // Save failed
    } finally {
      setSaving(false)
    }
  }

  async function handleDelete() {
    if (!deleteTarget) return

    setDeleting(true)
    try {
      await api.delete(`/api/tenants/sucursales/${deleteTarget.id}`)
      setDeleteTarget(null)
      await fetchSucursales()
    } catch {
      // Delete failed
    } finally {
      setDeleting(false)
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-12">
        <div className="animate-spin h-8 w-8 border-4 border-action-confirm border-t-transparent rounded-full" />
      </div>
    )
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
          Sucursales
        </h1>
        <Button variant="confirm" onClick={openCreateModal}>
          Nueva Sucursal
        </Button>
      </div>

      {/* Sucursales table */}
      <div className="bg-white dark:bg-dark-surface rounded-lg shadow overflow-hidden">
        <ResponsiveTable>
          <table className="w-full text-sm text-left">
            <thead className="text-xs uppercase bg-gray-100 dark:bg-dark-bg text-gray-600 dark:text-gray-400">
              <tr>
                <th className="px-4 py-3 sticky-col">Nombre</th>
                <th className="px-4 py-3">Dirección</th>
                <th className="px-4 py-3">Teléfono</th>
                <th className="px-4 py-3">Serie Facturación</th>
                <th className="px-4 py-3">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {sucursales.length === 0 ? (
                <tr>
                  <td
                    colSpan={5}
                    className="px-4 py-8 text-center text-gray-500 dark:text-gray-400"
                  >
                    No hay sucursales registradas.
                  </td>
                </tr>
              ) : (
                sucursales.map(suc => (
                  <tr
                    key={suc.id}
                    className="border-b border-gray-200 dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-dark-bg/50"
                  >
                    <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-medium sticky-col">
                      {suc.nombre}
                    </td>
                    <td className="px-4 py-3 text-gray-700 dark:text-gray-300">
                      {suc.direccion || '—'}
                    </td>
                    <td className="px-4 py-3 text-gray-700 dark:text-gray-300">
                      {suc.telefono || '—'}
                    </td>
                    <td className="px-4 py-3 text-gray-700 dark:text-gray-300">
                      {suc.serieFacturacion || '—'}
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex gap-2">
                        <Button
                          variant="edit"
                          className="text-xs px-2 py-1"
                          onClick={() => openEditModal(suc)}
                        >
                          Editar
                        </Button>
                        <Button
                          variant="danger"
                          className="text-xs px-2 py-1"
                          onClick={() => setDeleteTarget(suc)}
                        >
                          Eliminar
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </ResponsiveTable>
      </div>

      {/* Create/Edit Modal (Req 21.9) */}
      <Modal
        open={showModal}
        onClose={() => setShowModal(false)}
        title={editingSucursal ? 'Editar Sucursal' : 'Nueva Sucursal'}
        size="md"
      >
        <form
          onSubmit={e => {
            e.preventDefault()
            handleSubmit()
          }}
          className="space-y-4"
        >
          {/* Nombre */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Nombre *
            </label>
            <input
              type="text"
              value={nombre}
              onChange={e => setNombre(e.target.value)}
              className={`w-full rounded-lg border ${
                errors.nombre
                  ? 'border-action-danger'
                  : 'border-gray-300 dark:border-gray-600'
              } bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent`}
            />
            {errors.nombre && (
              <p className="mt-1 text-xs text-action-danger">{errors.nombre}</p>
            )}
          </div>

          {/* Dirección */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Dirección
            </label>
            <input
              type="text"
              value={direccion}
              onChange={e => setDireccion(e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            />
          </div>

          {/* Teléfono */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Teléfono
            </label>
            <input
              type="text"
              value={telefono}
              onChange={e => setTelefono(e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            />
          </div>

          {/* Serie Facturación */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Serie Facturación
            </label>
            <input
              type="text"
              value={serieFacturacion}
              onChange={e => setSerieFacturacion(e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            />
          </div>

          {/* Actions */}
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="neutral" type="button" onClick={() => setShowModal(false)}>
              Cancelar
            </Button>
            <Button variant="confirm" type="submit" loading={saving}>
              {editingSucursal ? 'Guardar Cambios' : 'Crear Sucursal'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Delete Confirm Dialog (Req 21.15, 5.5) */}
      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title="Eliminar Sucursal"
        confirmLabel="Eliminar"
        loading={deleting}
      >
        <p>
          ¿Está seguro de que desea eliminar la sucursal{' '}
          <strong>{deleteTarget?.nombre}</strong>?
        </p>
        <p className="mt-2 text-action-danger font-medium">
          ⚠️ Los usuarios asignados a esta sucursal quedarán sin sucursal asignada.
        </p>
      </ConfirmDialog>
    </div>
  )
}
