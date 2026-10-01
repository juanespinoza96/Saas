import { useState, useEffect } from 'react'
import { apiRequest } from '../lib/api'
import { Button } from '../components/ui/Button'
import { Modal } from '../components/ui/Modal'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'

interface Plan {
  id: number
  nombre: string
  precio: number
  limiteUsuarios: number
  limiteAtributos: number
}

interface EditForm {
  precio: string
  limiteUsuarios: string
  limiteAtributos: string
}

export function PlanesPage() {
  const [planes, setPlanes] = useState<Plan[]>([])
  const [loading, setLoading] = useState(true)
  const [editingPlan, setEditingPlan] = useState<Plan | null>(null)
  const [editForm, setEditForm] = useState<EditForm>({ precio: '', limiteUsuarios: '', limiteAtributos: '' })
  const [showConfirm, setShowConfirm] = useState(false)
  const [saving, setSaving] = useState(false)
  const [validationError, setValidationError] = useState('')

  const fetchPlanes = async () => {
    setLoading(true)
    try {
      const data = await apiRequest<Plan[]>('/api/admin/planes')
      setPlanes(data)
    } catch (error) {
      console.error('Error loading plans:', error)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchPlanes()
  }, [])

  const openEdit = (plan: Plan) => {
    setEditingPlan(plan)
    setEditForm({
      precio: plan.precio.toString(),
      limiteUsuarios: plan.limiteUsuarios.toString(),
      limiteAtributos: plan.limiteAtributos.toString(),
    })
    setValidationError('')
  }

  const closeEdit = () => {
    setEditingPlan(null)
    setValidationError('')
  }

  const handleSaveClick = () => {
    const precio = parseFloat(editForm.precio)
    if (isNaN(precio) || precio <= 0) {
      setValidationError('El precio debe ser mayor a 0')
      return
    }
    const limiteUsuarios = parseInt(editForm.limiteUsuarios)
    if (isNaN(limiteUsuarios) || (limiteUsuarios <= 0 && limiteUsuarios !== -1)) {
      setValidationError('Límite de usuarios debe ser mayor a 0 o -1 (ilimitado)')
      return
    }
    const limiteAtributos = parseInt(editForm.limiteAtributos)
    if (isNaN(limiteAtributos) || (limiteAtributos <= 0 && limiteAtributos !== -1)) {
      setValidationError('Límite de atributos debe ser mayor a 0 o -1 (ilimitado)')
      return
    }
    setValidationError('')
    setShowConfirm(true)
  }

  const handleConfirmSave = async () => {
    if (!editingPlan) return
    setSaving(true)
    try {
      await apiRequest(`/api/admin/planes/${editingPlan.id}`, {
        method: 'PUT',
        body: {
          precio: parseFloat(editForm.precio),
          limiteUsuarios: parseInt(editForm.limiteUsuarios),
          limiteAtributos: parseInt(editForm.limiteAtributos),
        },
      })
      setShowConfirm(false)
      closeEdit()
      await fetchPlanes()
    } catch (error) {
      console.error('Error updating plan:', error)
    } finally {
      setSaving(false)
    }
  }

  const formatLimit = (value: number): string => {
    return value === -1 ? 'Ilimitado' : value.toString()
  }

  if (loading) {
    return (
      <div className="space-y-6">
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Gestión de Planes</h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          {[1, 2, 3].map((i) => (
            <div key={i} className="animate-pulse bg-gray-200 dark:bg-dark-surface rounded-xl h-64"></div>
          ))}
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Gestión de Planes</h2>
        <p className="text-gray-500 dark:text-dark-text/70 mt-1">
          Ver y editar precios y límites de los tres planes de suscripción.
        </p>
      </div>

      {/* Plan Cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {planes.map((plan) => (
          <div
            key={plan.id}
            className="bg-white dark:bg-dark-surface rounded-xl p-6 shadow-sm border border-gray-100 dark:border-dark-surface flex flex-col"
          >
            <h3 className="text-lg font-semibold text-gray-900 dark:text-dark-text">
              {plan.nombre}
            </h3>
            <div className="mt-4 flex-1 space-y-3">
              <div>
                <span className="text-3xl font-bold text-gray-900 dark:text-dark-text">
                  ${plan.precio}
                </span>
                <span className="text-sm text-gray-500 dark:text-dark-text/60">/mes</span>
              </div>
              <div className="space-y-2 text-sm text-gray-600 dark:text-dark-text/70">
                <div className="flex justify-between">
                  <span>Usuarios</span>
                  <span className="font-medium text-gray-900 dark:text-dark-text">
                    {formatLimit(plan.limiteUsuarios)}
                  </span>
                </div>
                <div className="flex justify-between">
                  <span>Atributos</span>
                  <span className="font-medium text-gray-900 dark:text-dark-text">
                    {formatLimit(plan.limiteAtributos)}
                  </span>
                </div>
              </div>
            </div>
            <div className="mt-6">
              <Button variant="edit" onClick={() => openEdit(plan)} className="w-full">
                Editar
              </Button>
            </div>
          </div>
        ))}
      </div>

      {/* Edit Modal */}
      <Modal
        isOpen={editingPlan !== null}
        onClose={closeEdit}
        title={`Editar Plan: ${editingPlan?.nombre ?? ''}`}
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Precio mensual ($)
            </label>
            <input
              type="number"
              step="0.01"
              min="0.01"
              value={editForm.precio}
              onChange={(e) => setEditForm({ ...editForm, precio: e.target.value })}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Límite de Usuarios (-1 = ilimitado)
            </label>
            <input
              type="number"
              min="-1"
              value={editForm.limiteUsuarios}
              onChange={(e) => setEditForm({ ...editForm, limiteUsuarios: e.target.value })}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Límite de Atributos (-1 = ilimitado)
            </label>
            <input
              type="number"
              min="-1"
              value={editForm.limiteAtributos}
              onChange={(e) => setEditForm({ ...editForm, limiteAtributos: e.target.value })}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            />
          </div>

          {validationError && (
            <p className="text-sm text-red-500">{validationError}</p>
          )}

          <div className="flex justify-end gap-3">
            <Button variant="neutral" onClick={closeEdit}>
              Cancelar
            </Button>
            <Button variant="confirm" onClick={handleSaveClick}>
              Guardar Cambios
            </Button>
          </div>
        </div>
      </Modal>

      {/* Confirmation Dialog (Req 21.15) */}
      <ConfirmDialog
        isOpen={showConfirm}
        onClose={() => setShowConfirm(false)}
        onConfirm={handleConfirmSave}
        title="Confirmar Cambios de Plan"
        message={`Los cambios de precio se aplicarán en el próximo ciclo de facturación para todos los comercios con el plan "${editingPlan?.nombre}". ¿Desea continuar?`}
        confirmLabel="Confirmar Cambios"
        variant="confirm"
        loading={saving}
      />
    </div>
  )
}
