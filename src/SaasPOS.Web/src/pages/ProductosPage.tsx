import { useState, useEffect, useCallback, useMemo } from 'react'
import { api } from '../lib/api'
import { Modal } from '../components/ui/Modal'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Button } from '../components/ui/Button'
import { ResponsiveTable } from '../components/ui/ResponsiveTable'

// --- Types ---
interface Categoria {
  id: string
  nombre: string
}

interface AtributoCategoria {
  id: string
  nombreAtributo: string
  tipoDato: 'Texto' | 'Numero' | 'Boolean' | 'Fecha'
}

interface Producto {
  id: string
  nombre: string
  tipoArticulo: 'Venta Directa' | 'Insumo' | 'Ensamblado'
  categoriaId: string
  categoriaNombre?: string
  precioLista: number
  precioMinimo: number
  manejaStock: boolean
  unidadMedida: string
  costoProduccion: number
  valoresDinamicos?: Record<string, unknown>
}

interface RecetaItem {
  ingredienteId: string
  cantidadRequerida: number
}

interface FormErrors {
  [key: string]: string
}

// --- ProductosPage ---
export function ProductosPage() {
  const [productos, setProductos] = useState<Producto[]>([])
  const [categorias, setCategorias] = useState<Categoria[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [filterCategoria, setFilterCategoria] = useState('')

  // Modal state
  const [modalOpen, setModalOpen] = useState(false)
  const [editingProduct, setEditingProduct] = useState<Producto | null>(null)
  const [formDirty, setFormDirty] = useState(false)
  const [saving, setSaving] = useState(false)

  // Delete state
  const [deleteTarget, setDeleteTarget] = useState<Producto | null>(null)
  const [deleting, setDeleting] = useState(false)

  // Form state
  const [form, setForm] = useState({
    nombre: '',
    tipoArticulo: 'Venta Directa' as Producto['tipoArticulo'],
    categoriaId: '',
    precioLista: 0,
    precioMinimo: 0,
    manejaStock: false,
    unidadMedida: 'Unidad',
    costoProduccion: 0,
    valoresDinamicos: {} as Record<string, unknown>,
  })
  const [errors, setErrors] = useState<FormErrors>({})

  // Recipe state (for Ensamblado)
  const [receta, setReceta] = useState<RecetaItem[]>([])
  const [atributosCategoria, setAtributosCategoria] = useState<AtributoCategoria[]>([])

  // --- Data loading ---
  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const [prods, cats] = await Promise.all([
        api.get<Producto[]>('/api/tenants/productos'),
        api.get<Categoria[]>('/api/tenants/categorias'),
      ])
      setProductos(prods)
      setCategorias(cats)
    } catch {
      // Error handled silently
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadData() }, [loadData])

  // Load category attributes when categoriaId changes
  useEffect(() => {
    if (!form.categoriaId) {
      setAtributosCategoria([])
      return
    }
    api.get<AtributoCategoria[]>(`/api/tenants/categorias/${form.categoriaId}/atributos`)
      .then(setAtributosCategoria)
      .catch(() => setAtributosCategoria([]))
  }, [form.categoriaId])

  // --- Filtered products ---
  const filtered = useMemo(() => {
    return productos.filter(p => {
      const matchSearch = !search || p.nombre.toLowerCase().includes(search.toLowerCase())
      const matchCat = !filterCategoria || p.categoriaId === filterCategoria
      return matchSearch && matchCat
    })
  }, [productos, search, filterCategoria])

  // --- Insumos for recipe ---
  const insumos = useMemo(() => productos.filter(p => p.tipoArticulo === 'Insumo'), [productos])

  // --- Validation ---
  const validate = (): boolean => {
    const newErrors: FormErrors = {}
    if (!form.nombre.trim()) newErrors.nombre = 'El nombre es requerido'
    if (form.precioLista <= 0) newErrors.precioLista = 'El precio debe ser mayor a 0'
    if (form.precioMinimo > form.precioLista) newErrors.precioMinimo = 'El precio mínimo no puede ser mayor al precio de lista'
    if (!form.categoriaId) newErrors.categoriaId = 'Seleccione una categoría'
    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }

  // --- Modal actions ---
  const openCreate = () => {
    setEditingProduct(null)
    setForm({
      nombre: '',
      tipoArticulo: 'Venta Directa',
      categoriaId: '',
      precioLista: 0,
      precioMinimo: 0,
      manejaStock: false,
      unidadMedida: 'Unidad',
      costoProduccion: 0,
      valoresDinamicos: {},
    })
    setReceta([])
    setErrors({})
    setFormDirty(false)
    setModalOpen(true)
  }

  const openEdit = async (product: Producto) => {
    setEditingProduct(product)
    setForm({
      nombre: product.nombre,
      tipoArticulo: product.tipoArticulo,
      categoriaId: product.categoriaId,
      precioLista: product.precioLista,
      precioMinimo: product.precioMinimo,
      manejaStock: product.manejaStock,
      unidadMedida: product.unidadMedida,
      costoProduccion: product.costoProduccion,
      valoresDinamicos: product.valoresDinamicos ?? {},
    })
    setErrors({})
    setFormDirty(false)
    setModalOpen(true)

    if (product.tipoArticulo === 'Ensamblado') {
      try {
        const r = await api.get<RecetaItem[]>(`/api/tenants/productos/${product.id}/receta`)
        setReceta(r)
      } catch {
        setReceta([])
      }
    } else {
      setReceta([])
    }
  }

  const handleSave = async () => {
    if (!validate()) return
    setSaving(true)
    try {
      if (editingProduct) {
        await api.put(`/api/tenants/productos/${editingProduct.id}`, form)
        if (form.tipoArticulo === 'Ensamblado') {
          await api.post(`/api/tenants/productos/${editingProduct.id}/receta`, receta)
        }
      } else {
        const created = await api.post<Producto>('/api/tenants/productos', form)
        if (form.tipoArticulo === 'Ensamblado' && receta.length > 0) {
          await api.post(`/api/tenants/productos/${created.id}/receta`, receta)
        }
      }
      setModalOpen(false)
      await loadData()
    } catch {
      // Error handled silently
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      await api.delete(`/api/tenants/productos/${deleteTarget.id}`)
      setDeleteTarget(null)
      await loadData()
    } catch {
      // Error handled silently
    } finally {
      setDeleting(false)
    }
  }

  // --- Recipe helpers ---
  const addRecetaItem = () => {
    setReceta(prev => [...prev, { ingredienteId: '', cantidadRequerida: 1 }])
    setFormDirty(true)
  }

  const updateRecetaItem = (idx: number, field: keyof RecetaItem, value: string | number) => {
    setReceta(prev => prev.map((item, i) => i === idx ? { ...item, [field]: value } : item))
    setFormDirty(true)
  }

  const removeRecetaItem = (idx: number) => {
    setReceta(prev => prev.filter((_, i) => i !== idx))
    setFormDirty(true)
  }

  // --- Dynamic attribute render ---
  const renderDynamicField = (attr: AtributoCategoria) => {
    const value = form.valoresDinamicos[attr.nombreAtributo] ?? ''
    const onChange = (v: unknown) => {
      setForm(prev => ({
        ...prev,
        valoresDinamicos: { ...prev.valoresDinamicos, [attr.nombreAtributo]: v },
      }))
      setFormDirty(true)
    }

    switch (attr.tipoDato) {
      case 'Texto':
        return (
          <input
            type="text"
            value={value as string}
            onChange={e => onChange(e.target.value)}
            className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
          />
        )
      case 'Numero':
        return (
          <input
            type="number"
            value={value as number}
            onChange={e => onChange(Number(e.target.value))}
            className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
          />
        )
      case 'Boolean':
        return (
          <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
            <input
              type="checkbox"
              checked={!!value}
              onChange={e => onChange(e.target.checked)}
              className="rounded border-gray-300"
            />
            Sí
          </label>
        )
      case 'Fecha':
        return (
          <input
            type="date"
            value={value as string}
            onChange={e => onChange(e.target.value)}
            className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
          />
        )
    }
  }

  // --- Field update helper ---
  const updateField = (field: string, value: unknown) => {
    setForm(prev => ({ ...prev, [field]: value }))
    setFormDirty(true)
    if (errors[field]) setErrors(prev => { const n = { ...prev }; delete n[field]; return n })
  }

  const catNameMap = useMemo(() => {
    const m: Record<string, string> = {}
    categorias.forEach(c => { m[c.id] = c.nombre })
    return m
  }, [categorias])

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Productos</h1>
        <Button variant="confirm" onClick={openCreate}>Nuevo Producto</Button>
      </div>

      {/* Search and filter */}
      <div className="flex flex-col sm:flex-row gap-3 mb-4">
        <input
          type="text"
          placeholder="Buscar por nombre..."
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="flex-1 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
        />
        <select
          value={filterCategoria}
          onChange={e => setFilterCategoria(e.target.value)}
          className="rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
        >
          <option value="">Todas las categorías</option>
          {categorias.map(c => <option key={c.id} value={c.id}>{c.nombre}</option>)}
        </select>
      </div>

      {/* Product table */}
      {loading ? (
        <p className="text-gray-500 dark:text-gray-400">Cargando...</p>
      ) : (
        <ResponsiveTable className="rounded-lg border border-gray-200 dark:border-gray-700">
          <table className="w-full text-sm text-left">
            <thead className="bg-gray-50 dark:bg-dark-surface text-gray-600 dark:text-gray-300">
              <tr>
                <th className="px-4 py-3 sticky-col">Nombre</th>
                <th className="px-4 py-3">Tipo</th>
                <th className="px-4 py-3">Categoría</th>
                <th className="px-4 py-3 text-right">Precio Lista</th>
                <th className="px-4 py-3 text-right">Precio Mínimo</th>
                <th className="px-4 py-3 text-center">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200 dark:divide-gray-700">
              {filtered.map(p => (
                <tr key={p.id} className="bg-white dark:bg-dark-bg hover:bg-gray-50 dark:hover:bg-dark-surface/50">
                  <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-medium sticky-col">{p.nombre}</td>
                  <td className="px-4 py-3 text-gray-600 dark:text-gray-400">{p.tipoArticulo}</td>
                  <td className="px-4 py-3 text-gray-600 dark:text-gray-400">{catNameMap[p.categoriaId] ?? '-'}</td>
                  <td className="px-4 py-3 text-right text-gray-900 dark:text-dark-text">${p.precioLista.toFixed(2)}</td>
                  <td className="px-4 py-3 text-right text-gray-600 dark:text-gray-400">${p.precioMinimo.toFixed(2)}</td>
                  <td className="px-4 py-3 text-center">
                    <div className="flex justify-center gap-2">
                      <Button variant="edit" onClick={() => openEdit(p)} className="!px-3 !py-1 text-xs">Editar</Button>
                      <Button variant="danger" onClick={() => setDeleteTarget(p)} className="!px-3 !py-1 text-xs">Eliminar</Button>
                    </div>
                  </td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-500 dark:text-gray-400">No se encontraron productos</td></tr>
              )}
            </tbody>
          </table>
        </ResponsiveTable>
      )}

      {/* Create/Edit Modal */}
      <Modal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        title={editingProduct ? 'Editar Producto' : 'Nuevo Producto'}
        warnOnClose={formDirty}
        size="lg"
      >
        <div className="space-y-4 max-h-[70vh] overflow-y-auto pr-2">
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

          {/* Tipo Artículo */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Tipo de Artículo</label>
            <select
              value={form.tipoArticulo}
              onChange={e => updateField('tipoArticulo', e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            >
              <option value="Venta Directa">Venta Directa</option>
              <option value="Insumo">Insumo</option>
              <option value="Ensamblado">Ensamblado</option>
            </select>
          </div>

          {/* Categoría */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Categoría *</label>
            <select
              value={form.categoriaId}
              onChange={e => updateField('categoriaId', e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            >
              <option value="">Seleccionar categoría</option>
              {categorias.map(c => <option key={c.id} value={c.id}>{c.nombre}</option>)}
            </select>
            {errors.categoriaId && <p className="mt-1 text-xs text-action-danger">{errors.categoriaId}</p>}
          </div>

          {/* Precios */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Precio Lista *</label>
              <input
                type="number"
                min="0"
                step="0.01"
                value={form.precioLista}
                onChange={e => updateField('precioLista', Number(e.target.value))}
                className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
              />
              {errors.precioLista && <p className="mt-1 text-xs text-action-danger">{errors.precioLista}</p>}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Precio Mínimo</label>
              <input
                type="number"
                min="0"
                step="0.01"
                value={form.precioMinimo}
                onChange={e => updateField('precioMinimo', Number(e.target.value))}
                className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
              />
              {errors.precioMinimo && <p className="mt-1 text-xs text-action-danger">{errors.precioMinimo}</p>}
            </div>
          </div>

          {/* Maneja Stock & Unidad Medida */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="flex items-center gap-2 text-sm font-medium text-gray-700 dark:text-gray-300">
                <input
                  type="checkbox"
                  checked={form.manejaStock}
                  onChange={e => updateField('manejaStock', e.target.checked)}
                  className="rounded border-gray-300"
                />
                Maneja Stock
              </label>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Unidad de Medida</label>
              <input
                type="text"
                value={form.unidadMedida}
                onChange={e => updateField('unidadMedida', e.target.value)}
                className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
              />
            </div>
          </div>

          {/* Costo Producción */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Costo de Producción</label>
            <input
              type="number"
              min="0"
              step="0.01"
              value={form.costoProduccion}
              onChange={e => updateField('costoProduccion', Number(e.target.value))}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
            />
          </div>

          {/* Dynamic Attributes from Category */}
          {atributosCategoria.length > 0 && (
            <div className="border-t border-gray-200 dark:border-gray-700 pt-4">
              <h4 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">Atributos de Categoría</h4>
              <div className="space-y-3">
                {atributosCategoria.map(attr => (
                  <div key={attr.id}>
                    <label className="block text-sm text-gray-600 dark:text-gray-400 mb-1">{attr.nombreAtributo}</label>
                    {renderDynamicField(attr)}
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Recipe section for Ensamblado */}
          {form.tipoArticulo === 'Ensamblado' && (
            <div className="border-t border-gray-200 dark:border-gray-700 pt-4">
              <div className="flex items-center justify-between mb-3">
                <h4 className="text-sm font-semibold text-gray-700 dark:text-gray-300">Receta (Ingredientes)</h4>
                <Button variant="confirm" onClick={addRecetaItem} className="!px-3 !py-1 text-xs">+ Ingrediente</Button>
              </div>
              {receta.length === 0 && (
                <p className="text-xs text-gray-500 dark:text-gray-400">No hay ingredientes definidos.</p>
              )}
              <div className="space-y-2">
                {receta.map((item, idx) => (
                  <div key={idx} className="flex gap-2 items-center">
                    <select
                      value={item.ingredienteId}
                      onChange={e => updateRecetaItem(idx, 'ingredienteId', e.target.value)}
                      className="flex-1 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
                    >
                      <option value="">Seleccionar insumo</option>
                      {insumos.map(ins => <option key={ins.id} value={ins.id}>{ins.nombre}</option>)}
                    </select>
                    <input
                      type="number"
                      min="0.01"
                      step="0.01"
                      value={item.cantidadRequerida}
                      onChange={e => updateRecetaItem(idx, 'cantidadRequerida', Number(e.target.value))}
                      className="w-24 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text"
                      placeholder="Cantidad"
                    />
                    <Button variant="danger" onClick={() => removeRecetaItem(idx)} className="!px-2 !py-1 text-xs">✕</Button>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Action buttons */}
          <div className="flex justify-end gap-3 pt-4 border-t border-gray-200 dark:border-gray-700">
            <Button variant="neutral" onClick={() => setModalOpen(false)} disabled={saving}>Cancelar</Button>
            <Button variant="confirm" onClick={handleSave} loading={saving}>
              {editingProduct ? 'Guardar Cambios' : 'Crear Producto'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Delete Confirmation */}
      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title="Eliminar Producto"
        confirmLabel="Eliminar"
        loading={deleting}
      >
        <p>¿Estás seguro de eliminar el producto <strong>{deleteTarget?.nombre}</strong>? Esta acción no se puede deshacer.</p>
      </ConfirmDialog>
    </div>
  )
}
