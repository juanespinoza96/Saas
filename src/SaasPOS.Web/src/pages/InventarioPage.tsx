import { useState, useEffect, useCallback } from 'react'
import { api } from '../lib/api'
import { Modal } from '../components/ui/Modal'
import { Button } from '../components/ui/Button'
import { ResponsiveTable } from '../components/ui/ResponsiveTable'

// --- Types ---
interface Sucursal {
  id: string
  nombre: string
}

interface StockItem {
  productoId: string
  productoNombre: string
  cantidadFisica: number
  unidadMedida?: string
}

// --- InventarioPage ---
export function InventarioPage() {
  const [sucursales, setSucursales] = useState<Sucursal[]>([])
  const [selectedSucursal, setSelectedSucursal] = useState('')
  const [stock, setStock] = useState<StockItem[]>([])
  const [loading, setLoading] = useState(true)
  const [loadingStock, setLoadingStock] = useState(false)

  // Adjust modal
  const [adjustTarget, setAdjustTarget] = useState<StockItem | null>(null)
  const [newCantidad, setNewCantidad] = useState(0)
  const [adjustError, setAdjustError] = useState('')
  const [adjustSaving, setAdjustSaving] = useState(false)
  const [confirmStep, setConfirmStep] = useState(false)

  // --- Load sucursales ---
  const loadSucursales = useCallback(async () => {
    setLoading(true)
    try {
      const sucs = await api.get<Sucursal[]>('/api/tenants/sucursales')
      setSucursales(sucs)
      if (sucs.length > 0 && sucs[0]) {
        setSelectedSucursal(sucs[0].id)
      }
    } catch {
      // handled silently
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadSucursales() }, [loadSucursales])

  // --- Load stock for selected sucursal ---
  const loadStock = useCallback(async (sucursalId: string) => {
    if (!sucursalId) return
    setLoadingStock(true)
    try {
      const items = await api.get<StockItem[]>(`/api/tenants/stock/sucursal/${sucursalId}`)
      setStock(items)
    } catch {
      setStock([])
    } finally {
      setLoadingStock(false)
    }
  }, [])

  useEffect(() => {
    if (selectedSucursal) {
      loadStock(selectedSucursal)
    }
  }, [selectedSucursal, loadStock])

  // --- Adjust stock ---
  const openAdjust = (item: StockItem) => {
    setAdjustTarget(item)
    setNewCantidad(item.cantidadFisica)
    setAdjustError('')
    setConfirmStep(false)
  }

  const handleAdjustSubmit = () => {
    if (newCantidad < 0) {
      setAdjustError('La cantidad no puede ser negativa')
      return
    }
    setAdjustError('')
    // Show confirmation step before saving
    setConfirmStep(true)
  }

  const handleAdjustConfirm = async () => {
    if (!adjustTarget) return
    setAdjustSaving(true)
    try {
      await api.patch(`/api/tenants/stock/${adjustTarget.productoId}/sucursal/${selectedSucursal}`, {
        cantidadFisica: newCantidad,
      })
      setAdjustTarget(null)
      setConfirmStep(false)
      await loadStock(selectedSucursal)
    } catch {
      // handled silently
    } finally {
      setAdjustSaving(false)
    }
  }

  const closeAdjustModal = () => {
    setAdjustTarget(null)
    setConfirmStep(false)
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-6">Inventario / Stock</h1>

      {loading ? (
        <p className="text-gray-500 dark:text-gray-400">Cargando...</p>
      ) : (
        <>
          {/* Sucursal selector */}
          <div className="mb-4">
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Sucursal</label>
            <select
              value={selectedSucursal}
              onChange={e => setSelectedSucursal(e.target.value)}
              className="rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text w-full sm:w-auto sm:min-w-[240px]"
            >
              {sucursales.map(s => (
                <option key={s.id} value={s.id}>{s.nombre}</option>
              ))}
            </select>
          </div>

          {/* Stock table */}
          {loadingStock ? (
            <p className="text-gray-500 dark:text-gray-400">Cargando stock...</p>
          ) : (
            <ResponsiveTable className="rounded-lg border border-gray-200 dark:border-gray-700">
              <table className="w-full text-sm text-left">
                <thead className="bg-gray-50 dark:bg-dark-surface text-gray-600 dark:text-gray-300">
                  <tr>
                    <th className="px-4 py-3 sticky-col">Producto</th>
                    <th className="px-4 py-3 text-right">Cantidad Física</th>
                    <th className="px-4 py-3">Unidad</th>
                    <th className="px-4 py-3 text-center">Acciones</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200 dark:divide-gray-700">
                  {stock.map(item => (
                    <tr key={item.productoId} className="bg-white dark:bg-dark-bg hover:bg-gray-50 dark:hover:bg-dark-surface/50">
                      <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-medium sticky-col">{item.productoNombre}</td>
                      <td className="px-4 py-3 text-right text-gray-900 dark:text-dark-text">{item.cantidadFisica}</td>
                      <td className="px-4 py-3 text-gray-600 dark:text-gray-400">{item.unidadMedida ?? 'Unidad'}</td>
                      <td className="px-4 py-3 text-center">
                        <Button variant="edit" onClick={() => openAdjust(item)} className="!px-3 !py-1 text-xs">Ajustar Stock</Button>
                      </td>
                    </tr>
                  ))}
                  {stock.length === 0 && (
                    <tr><td colSpan={4} className="px-4 py-8 text-center text-gray-500 dark:text-gray-400">No hay productos con stock en esta sucursal</td></tr>
                  )}
                </tbody>
              </table>
            </ResponsiveTable>
          )}
        </>
      )}

      {/* Adjust Stock Modal */}
      <Modal
        open={!!adjustTarget}
        onClose={closeAdjustModal}
        title="Ajustar Stock"
        size="sm"
      >
        {adjustTarget && !confirmStep && (
          <div className="space-y-4">
            <p className="text-sm text-gray-600 dark:text-gray-400">
              Producto: <strong className="text-gray-900 dark:text-dark-text">{adjustTarget.productoNombre}</strong>
            </p>
            <p className="text-sm text-gray-600 dark:text-gray-400">
              Cantidad actual: <strong className="text-gray-900 dark:text-dark-text">{adjustTarget.cantidadFisica}</strong>
            </p>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Nueva Cantidad Física</label>
              <input
                type="number"
                min="0"
                step="1"
                value={newCantidad}
                onChange={e => { setNewCantidad(Number(e.target.value)); setAdjustError('') }}
                className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
              />
              {adjustError && <p className="mt-1 text-xs text-action-danger">{adjustError}</p>}
            </div>
            <div className="flex justify-end gap-3">
              <Button variant="neutral" onClick={closeAdjustModal}>Cancelar</Button>
              <Button variant="confirm" onClick={handleAdjustSubmit}>Continuar</Button>
            </div>
          </div>
        )}

        {/* Confirmation step */}
        {adjustTarget && confirmStep && (
          <div className="space-y-4">
            <p className="text-sm text-gray-600 dark:text-gray-400">
              Confirme el ajuste de stock para <strong className="text-gray-900 dark:text-dark-text">{adjustTarget.productoNombre}</strong>:
            </p>
            <div className="bg-gray-50 dark:bg-dark-bg rounded-lg p-3 text-sm space-y-1">
              <p className="text-gray-600 dark:text-gray-400">Cantidad anterior: <strong>{adjustTarget.cantidadFisica}</strong></p>
              <p className="text-gray-600 dark:text-gray-400">Nueva cantidad: <strong>{newCantidad}</strong></p>
              <p className="text-gray-600 dark:text-gray-400">
                Diferencia: <strong className={newCantidad >= adjustTarget.cantidadFisica ? 'text-action-confirm' : 'text-action-danger'}>
                  {newCantidad >= adjustTarget.cantidadFisica ? '+' : ''}{newCantidad - adjustTarget.cantidadFisica}
                </strong>
              </p>
            </div>
            <div className="flex justify-end gap-3">
              <Button variant="neutral" onClick={() => setConfirmStep(false)} disabled={adjustSaving}>Volver</Button>
              <Button variant="confirm" onClick={handleAdjustConfirm} loading={adjustSaving}>Confirmar Ajuste</Button>
            </div>
          </div>
        )}
      </Modal>
    </div>
  )
}
