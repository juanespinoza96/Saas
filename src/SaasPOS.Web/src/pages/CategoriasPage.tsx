import { useState, useEffect, useCallback } from 'react'
import { api } from '../lib/api'
import { Modal } from '../components/ui/Modal'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Button } from '../components/ui/Button'

// --- Types ---
interface Categoria {
  id: string
  nombre: string
  atributosCount?: number
}

interface AtributoCategoria {
  id: string
  nombreAtributo: string
  tipoDato: 'Texto' | 'Numero' | 'Boolean' | 'Fecha'
}

type TipoDato = 'Texto' | 'Numero' | 'Boolean' | 'Fecha'

interface FormErrors {
  [key: string]: string
}

// --- CategoriasPage ---
export function CategoriasPage() {
  const [categorias, setCategorias] = useState<Categoria[]>([])
  const [loading, setLoading] = useState(true)

  // Category modal
  const [catModalOpen, setCatModalOpen] = useState(false)
  const [editingCat, setEditingCat] = useState<Categoria | null>(null)
  const [catName, setCatName] = useState('')
  const [catErrors, setCatErrors] = useState<FormErrors>({})
  const [catDirty, setCatDirty] = useState(false)
  const [catSaving, setCatSaving] = useState(false)

  // Delete category
  const [deleteCatTarget, setDeleteCatTarget] = useState<Categoria | null>(null)
  const [deletingCat, setDeletingCat] = useState(false)

  // Expanded category (attributes)
  const [expandedCatId, setExpandedCatId] = useState<string | null>(null)
  const [atributos, setAtributos] = useState<AtributoCategoria[]>([])
  const [loadingAttrs, setLoadingAttrs] = useState(false)

  // Add attribute modal
  const [attrModalOpen, setAttrModalOpen] = useState(false)
  const [attrForm, setAttrForm] = useState({ nombreAtributo: '', tipoDato: 'Texto' as TipoDato })
  const [attrErrors, setAttrErrors] = useState<FormErrors>({})
  const [attrSaving, setAttrSaving] = useState(false)

  // Delete attribute
  const [deleteAttrTarget, setDeleteAttrTarget] = useState<AtributoCategoria | null>(null)
  const [deletingAttr, setDeletingAttr] = useState(false)

  // --- Data loading ---
  const loadCategorias = useCallback(async () => {
    setLoading(true)
    try {
      const cats = await api.get<Categoria[]>('/api/tenants/categorias')
      setCategorias(cats)
    } catch {
      // handled silently
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadCategorias() }, [loadCategorias])

  const loadAtributos = useCallback(async (catId: string) => {
    setLoadingAttrs(true)
    try {
      const attrs = await api.get<AtributoCategoria[]>(`/api/tenants/categorias/${catId}/atributos`)
      setAtributos(attrs)
    } catch {
      setAtributos([])
    } finally {
      setLoadingAttrs(false)
    }
  }, [])

  // --- Category CRUD ---
  const openCreateCat = () => {
    setEditingCat(null)
    setCatName('')
    setCatErrors({})
    setCatDirty(false)
    setCatModalOpen(true)
  }

  const openEditCat = (cat: Categoria) => {
    setEditingCat(cat)
    setCatName(cat.nombre)
    setCatErrors({})
    setCatDirty(false)
    setCatModalOpen(true)
  }

  const saveCat = async () => {
    const errors: FormErrors = {}
    if (!catName.trim()) errors.nombre = 'El nombre es requerido'
    setCatErrors(errors)
    if (Object.keys(errors).length > 0) return

    setCatSaving(true)
    try {
      if (editingCat) {
        await api.put(`/api/tenants/categorias/${editingCat.id}`, { nombre: catName })
      } else {
        await api.post('/api/tenants/categorias', { nombre: catName })
      }
      setCatModalOpen(false)
      await loadCategorias()
    } catch {
      // handled silently
    } finally {
      setCatSaving(false)
    }
  }

  const handleDeleteCat = async () => {
    if (!deleteCatTarget) return
    setDeletingCat(true)
    try {
      await api.delete(`/api/tenants/categorias/${deleteCatTarget.id}`)
      setDeleteCatTarget(null)
      if (expandedCatId === deleteCatTarget.id) {
        setExpandedCatId(null)
        setAtributos([])
      }
      await loadCategorias()
    } catch {
      // handled silently
    } finally {
      setDeletingCat(false)
    }
  }

  // --- Expand / collapse category ---
  const toggleExpand = (catId: string) => {
    if (expandedCatId === catId) {
      setExpandedCatId(null)
      setAtributos([])
    } else {
      setExpandedCatId(catId)
      loadAtributos(catId)
    }
  }

  // --- Attribute CRUD ---
  const openAddAttr = () => {
    setAttrForm({ nombreAtributo: '', tipoDato: 'Texto' })
    setAttrErrors({})
    setAttrModalOpen(true)
  }

  const saveAttr = async () => {
    const errors: FormErrors = {}
    if (!attrForm.nombreAtributo.trim()) errors.nombreAtributo = 'El nombre es requerido'
    setAttrErrors(errors)
    if (Object.keys(errors).length > 0) return

    setAttrSaving(true)
    try {
      await api.post(`/api/tenants/categorias/${expandedCatId}/atributos`, attrForm)
      setAttrModalOpen(false)
      await loadAtributos(expandedCatId!)
      await loadCategorias()
    } catch {
      // handled silently
    } finally {
      setAttrSaving(false)
    }
  }

  const handleDeleteAttr = async () => {
    if (!deleteAttrTarget || !expandedCatId) return
    setDeletingAttr(true)
    try {
      await api.delete(`/api/tenants/categorias/${expandedCatId}/atributos/${deleteAttrTarget.id}`)
      setDeleteAttrTarget(null)
      await loadAtributos(expandedCatId)
      await loadCategorias()
    } catch {
      // handled silently
    } finally {
      setDeletingAttr(false)
    }
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Categorías y Atributos</h1>
        <Button variant="confirm" onClick={openCreateCat}>Nueva Categoría</Button>
      </div>

      {loading ? (
        <p className="text-gray-500 dark:text-gray-400">Cargando...</p>
      ) : (
        <div className="space-y-2">
          {categorias.length === 0 && (
            <p className="text-gray-500 dark:text-gray-400 text-center py-8">No hay categorías definidas.</p>
          )}
          {categorias.map(cat => (
            <div key={cat.id} className="border border-gray-200 dark:border-gray-700 rounded-lg overflow-hidden">
              {/* Category row */}
              <div className="flex items-center justify-between px-4 py-3 bg-white dark:bg-dark-bg hover:bg-gray-50 dark:hover:bg-dark-surface/50">
                <button
                  onClick={() => toggleExpand(cat.id)}
                  className="flex items-center gap-3 text-left flex-1"
                >
                  <svg
                    className={`h-4 w-4 text-gray-400 transition-transform ${expandedCatId === cat.id ? 'rotate-90' : ''}`}
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                  >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                  </svg>
                  <span className="font-medium text-gray-900 dark:text-dark-text">{cat.nombre}</span>
                  <span className="text-xs text-gray-500 dark:text-gray-400">
                    ({cat.atributosCount ?? 0} atributos)
                  </span>
                </button>
                <div className="flex gap-2">
                  <Button variant="edit" onClick={() => openEditCat(cat)} className="!px-3 !py-1 text-xs">Editar</Button>
                  <Button variant="danger" onClick={() => setDeleteCatTarget(cat)} className="!px-3 !py-1 text-xs">Eliminar</Button>
                </div>
              </div>

              {/* Expanded attributes section */}
              {expandedCatId === cat.id && (
                <div className="px-4 py-3 bg-gray-50 dark:bg-dark-surface border-t border-gray-200 dark:border-gray-700">
                  <div className="flex items-center justify-between mb-3">
                    <h4 className="text-sm font-semibold text-gray-700 dark:text-gray-300">Atributos</h4>
                    <Button variant="confirm" onClick={openAddAttr} className="!px-3 !py-1 text-xs">+ Atributo</Button>
                  </div>
                  {loadingAttrs ? (
                    <p className="text-sm text-gray-500 dark:text-gray-400">Cargando atributos...</p>
                  ) : atributos.length === 0 ? (
                    <p className="text-sm text-gray-500 dark:text-gray-400">No hay atributos definidos para esta categoría.</p>
                  ) : (
                    <div className="space-y-2">
                      {atributos.map(attr => (
                        <div key={attr.id} className="flex items-center justify-between bg-white dark:bg-dark-bg rounded-lg px-3 py-2 border border-gray-200 dark:border-gray-600">
                          <div>
                            <span className="text-sm font-medium text-gray-900 dark:text-dark-text">{attr.nombreAtributo}</span>
                            <span className="ml-2 text-xs text-gray-500 dark:text-gray-400 bg-gray-100 dark:bg-dark-surface px-2 py-0.5 rounded">{attr.tipoDato}</span>
                          </div>
                          <Button variant="danger" onClick={() => setDeleteAttrTarget(attr)} className="!px-2 !py-1 text-xs">Eliminar</Button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {/* Category Create/Edit Modal */}
      <Modal
        open={catModalOpen}
        onClose={() => setCatModalOpen(false)}
        title={editingCat ? 'Editar Categoría' : 'Nueva Categoría'}
        warnOnClose={catDirty}
        size="sm"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Nombre *</label>
            <input
              type="text"
              value={catName}
              onChange={e => { setCatName(e.target.value); setCatDirty(true); if (catErrors.nombre) setCatErrors({}) }}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            />
            {catErrors.nombre && <p className="mt-1 text-xs text-action-danger">{catErrors.nombre}</p>}
          </div>
          <div className="flex justify-end gap-3">
            <Button variant="neutral" onClick={() => setCatModalOpen(false)} disabled={catSaving}>Cancelar</Button>
            <Button variant="confirm" onClick={saveCat} loading={catSaving}>
              {editingCat ? 'Guardar' : 'Crear'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Add Attribute Modal */}
      <Modal
        open={attrModalOpen}
        onClose={() => setAttrModalOpen(false)}
        title="Agregar Atributo"
        size="sm"
      >
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Nombre del Atributo *</label>
            <input
              type="text"
              value={attrForm.nombreAtributo}
              onChange={e => { setAttrForm(prev => ({ ...prev, nombreAtributo: e.target.value })); if (attrErrors.nombreAtributo) setAttrErrors({}) }}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            />
            {attrErrors.nombreAtributo && <p className="mt-1 text-xs text-action-danger">{attrErrors.nombreAtributo}</p>}
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Tipo de Dato</label>
            <select
              value={attrForm.tipoDato}
              onChange={e => setAttrForm(prev => ({ ...prev, tipoDato: e.target.value as TipoDato }))}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            >
              <option value="Texto">Texto</option>
              <option value="Numero">Número</option>
              <option value="Boolean">Boolean</option>
              <option value="Fecha">Fecha</option>
            </select>
          </div>
          <div className="flex justify-end gap-3">
            <Button variant="neutral" onClick={() => setAttrModalOpen(false)} disabled={attrSaving}>Cancelar</Button>
            <Button variant="confirm" onClick={saveAttr} loading={attrSaving}>Agregar</Button>
          </div>
        </div>
      </Modal>

      {/* Delete Category Confirmation */}
      <ConfirmDialog
        open={!!deleteCatTarget}
        onClose={() => setDeleteCatTarget(null)}
        onConfirm={handleDeleteCat}
        title="Eliminar Categoría"
        confirmLabel="Eliminar"
        loading={deletingCat}
      >
        <p>¿Estás seguro de eliminar la categoría <strong>{deleteCatTarget?.nombre}</strong>? Se eliminarán también sus atributos asociados.</p>
      </ConfirmDialog>

      {/* Delete Attribute Confirmation */}
      <ConfirmDialog
        open={!!deleteAttrTarget}
        onClose={() => setDeleteAttrTarget(null)}
        onConfirm={handleDeleteAttr}
        title="Eliminar Atributo"
        confirmLabel="Eliminar"
        loading={deletingAttr}
      >
        <p>¿Estás seguro de eliminar el atributo <strong>{deleteAttrTarget?.nombreAtributo}</strong>? Los productos que lo usen perderán este valor.</p>
      </ConfirmDialog>
    </div>
  )
}
