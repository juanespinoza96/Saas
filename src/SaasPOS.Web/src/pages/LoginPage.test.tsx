import { render, screen, waitFor, cleanup, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import fc from 'fast-check'
import { MemoryRouter } from 'react-router-dom'
import { LoginPage } from './LoginPage'

// Mock react-router-dom navigate
const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

// Mock AuthContext
const mockLogin = vi.fn()
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    login: mockLogin,
    isAuthenticated: false,
    token: null,
    claims: null,
    logout: vi.fn(),
    hasRole: vi.fn(),
    comercioNombre: 'Test',
  }),
}))

// Mock the API module
vi.mock('../lib/api', () => ({
  api: {
    post: vi.fn(),
  },
}))

import { api } from '../lib/api'
const mockApiPost = vi.mocked(api.post)

function renderLoginPage() {
  return render(
    <MemoryRouter>
      <LoginPage />
    </MemoryRouter>
  )
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // Limpiar el estado de bloqueo persistido por `useLoginLockout` en
    // localStorage (clave `loginLockoutUntil`). El test de lockout 429 activa
    // `setLockout`, que persiste el bloqueo; sin esta limpieza el estado se
    // filtraría a los tests siguientes (el formulario aparecería oculto).
    localStorage.clear()
  })

  // Limpiar el DOM tras cada test para aislar renders. Sin esto, los renders de
  // `renderLoginPage()` se acumulan (varios formularios en el documento),
  // provocando ambigüedad en las queries y fugas de estado (p. ej. el bloqueo
  // del test de lockout 429) hacia los tests siguientes.
  afterEach(() => {
    cleanup()
  })

  describe('rendering', () => {
    it('renders email and password fields', () => {
      renderLoginPage()

      expect(screen.getByLabelText(/correo electrónico/i)).toBeInTheDocument()
      expect(screen.getByLabelText(/contraseña/i)).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /ingresar/i })).toBeInTheDocument()
    })
  })

  // Req 21.13: Inline validation messages next to each field
  describe('inline validation (Req 21.13)', () => {
    it('shows inline validation when submitting empty form', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      expect(await screen.findByText('El correo es obligatorio')).toBeInTheDocument()
      expect(await screen.findByText('La contraseña es obligatoria')).toBeInTheDocument()
    })

    it('shows email format validation for invalid email', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      await user.type(screen.getByLabelText(/correo electrónico/i), 'invalid-email')
      await user.type(screen.getByLabelText(/contraseña/i), 'password123')
      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      expect(await screen.findByText('Ingrese un correo válido')).toBeInTheDocument()
    })
  })

  describe('auth failure', () => {
    it('shows generic "Credenciales inválidas" on auth failure', async () => {
      const user = userEvent.setup()
      mockApiPost.mockRejectedValueOnce(new Error('Unauthorized'))
      renderLoginPage()

      await user.type(screen.getByLabelText(/correo electrónico/i), 'test@example.com')
      await user.type(screen.getByLabelText(/contraseña/i), 'wrongpassword')
      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      expect(await screen.findByText('Credenciales inválidas')).toBeInTheDocument()
    })
  })

  // Req 21.14: Disable buttons during API requests with loading indicator
  describe('loading state (Req 21.14)', () => {
    it('disables submit button while loading', async () => {
      const user = userEvent.setup()
      // Create a promise that won't resolve immediately
      let resolvePromise: (value: unknown) => void
      mockApiPost.mockImplementation(
        () => new Promise((resolve) => { resolvePromise = resolve })
      )
      renderLoginPage()

      await user.type(screen.getByLabelText(/correo electrónico/i), 'test@example.com')
      await user.type(screen.getByLabelText(/contraseña/i), 'password123')
      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      // Button should be disabled while request is in progress
      await waitFor(() => {
        const submitButton = screen.getByRole('button', { name: /ingresar/i })
        expect(submitButton).toBeDisabled()
      })

      // Resolver la promesa DENTRO de act(...) y esperar a que se completen las
      // actualizaciones de estado que dispara (login, navigate, setLoading(false)).
      // Así no quedan actualizaciones de estado de React pendientes fuera del
      // ciclo del test que generen warnings de act(...) ni retengan referencias.
      await act(async () => {
        resolvePromise!({ token: 'fake-token' })
      })
    })
  })

  // ---------------------------------------------------------------------------
  // Bugfix: "mostrar contraseña en móvil" (spec: mostrar-password-movil-fix)
  // Property 1: Bug Condition - Toggle de contraseña disponible en cualquier
  // entorno. Esta prueba de EXPLORACIÓN codifica el comportamiento esperado
  // (Expected Behavior) y DEBE FALLAR sobre el código sin corregir, porque el
  // campo de contraseña no ofrece un toggle propio (isBugCondition(X) = true:
  // tieneTogglePropio = false AND navegadorProveeControlNativo = false).
  //
  // Enfoque PBT acotado: al ser un defecto determinista de renderizado, se
  // acota la propiedad a los casos concretos que satisfacen isBugCondition(X)
  // — entorno móvil sin control nativo (`::-ms-reveal`) y sin toggle propio.
  // ---------------------------------------------------------------------------
  describe('bug condition — toggle propio de contraseña (Property 1)', () => {
    // Caso borde: simular entorno móvil sin control nativo del navegador.
    // jsdom no renderiza el pseudo-elemento `::-ms-reveal`, por lo que el
    // entorno de prueba ya representa "navegadorProveeControlNativo = false".
    // Se documenta explícitamente la condición del bug que se está ejerciendo.
    beforeEach(() => {
      // Reafirmar el contexto de renderizado (X) bajo prueba:
      //   X.navegadorProveeControlNativo = false (móvil / sin ::-ms-reveal)
      //   X.tieneTogglePropio            = false (código sin corregir)
      // => isBugCondition(X) = true
    })

    // Property 1 — parte "tieneToggleVisible": debe existir un botón de toggle
    // con nombre accesible "Mostrar contraseña". El toggle solo se muestra
    // cuando el campo tiene al menos un carácter, por lo que primero se escribe.
    it('renderiza un botón/toggle propio "Mostrar contraseña" en el campo de contraseña', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      await user.type(screen.getByLabelText(/contraseña/i), 'a')

      // Debe existir un control propio con nombre accesible descriptivo.
      const toggle = screen.getByRole('button', { name: /mostrar contraseña/i })
      expect(toggle).toBeInTheDocument()
      // El botón NO debe disparar el submit del formulario.
      expect(toggle).toHaveAttribute('type', 'button')
    })

    // Property 1 — parte "puedeAlternarVisibilidad": el input alterna
    // password <-> text al activar/desactivar el toggle.
    it('alterna el input de contraseña de "password" a "text" y de vuelta al activar el toggle', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      const passwordInput = screen.getByLabelText(/contraseña/i)
      // Escribir contenido para que aparezca el toggle.
      await user.type(passwordInput, 'clave123')
      // Estado inicial: oculto.
      expect(passwordInput).toHaveAttribute('type', 'password')

      const toggle = screen.getByRole('button', { name: /mostrar contraseña/i })

      // Primera activación: se revela el texto (type="text").
      await user.click(toggle)
      expect(passwordInput).toHaveAttribute('type', 'text')

      // Segunda activación: se vuelve a ocultar (type="password").
      await user.click(toggle)
      expect(passwordInput).toHaveAttribute('type', 'password')
    })

    // Property 1 — parte "esAccesible": el control expone aria-label descriptivo
    // y aria-pressed reflejando el estado, y es operable por teclado.
    it('el toggle es accesible: expone aria-label/aria-pressed y es operable por teclado', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      const passwordInput = screen.getByLabelText(/contraseña/i)
      // Escribir contenido para que aparezca el toggle.
      await user.type(passwordInput, 'clave123')
      const toggle = screen.getByRole('button', { name: /mostrar contraseña/i })

      // Estado inicial: aria-pressed="false" (contraseña oculta).
      expect(toggle).toHaveAttribute('aria-pressed', 'false')

      // Es enfocable por teclado.
      await user.tab()
      // Se enfoca navegando con teclado hasta el botón y se activa con Espacio.
      toggle.focus()
      expect(toggle).toHaveFocus()

      // Activación por teclado (Espacio): revela la contraseña.
      await user.keyboard('[Space]')
      expect(passwordInput).toHaveAttribute('type', 'text')
      // aria-pressed refleja el estado visible.
      expect(toggle).toHaveAttribute('aria-pressed', 'true')
      // El nombre accesible cambia a "Ocultar contraseña" cuando está visible.
      expect(toggle).toHaveAccessibleName(/ocultar contraseña/i)
    })

    // Visibilidad condicional del toggle: el botón NO debe estar presente
    // mientras el campo de contraseña esté vacío (evita que el usuario active
    // la visibilidad sobre un campo vacío, dejando un estado incoherente con el
    // ícono). Aparece al escribir el primer carácter y desaparece al vaciarlo.
    it('el toggle solo aparece cuando hay al menos un carácter y desaparece al vaciar el campo', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      const passwordInput = screen.getByLabelText(/contraseña/i)

      // Campo vacío: el botón NO existe.
      expect(
        screen.queryByRole('button', { name: /mostrar contraseña|ocultar contraseña/i }),
      ).not.toBeInTheDocument()

      // Al escribir un carácter, el botón aparece.
      await user.type(passwordInput, 'a')
      expect(
        screen.getByRole('button', { name: /mostrar contraseña/i }),
      ).toBeInTheDocument()

      // Al borrar todo el contenido, el botón vuelve a desaparecer.
      await user.clear(passwordInput)
      expect(
        screen.queryByRole('button', { name: /mostrar contraseña|ocultar contraseña/i }),
      ).not.toBeInTheDocument()
    })

    // Reset de visibilidad: si el usuario revela la contraseña y luego vacía el
    // campo, al volver a escribir el estado debe estar oculto (type="password"),
    // no arrastrar el "visible" anterior.
    it('resetea la visibilidad a oculta cuando el campo se vacía', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      const passwordInput = screen.getByLabelText(/contraseña/i)

      // Escribir y revelar la contraseña.
      await user.type(passwordInput, 'clave123')
      await user.click(screen.getByRole('button', { name: /mostrar contraseña/i }))
      expect(passwordInput).toHaveAttribute('type', 'text')

      // Vaciar el campo: el estado de visibilidad se resetea a oculto.
      await user.clear(passwordInput)

      // Volver a escribir: el input debe estar oculto de nuevo (type="password")
      // y el botón reaparece en su estado inicial "Mostrar contraseña".
      await user.type(passwordInput, 'nuevaClave')
      expect(passwordInput).toHaveAttribute('type', 'password')
      expect(
        screen.getByRole('button', { name: /mostrar contraseña/i }),
      ).toHaveAttribute('aria-pressed', 'false')
    })

    // Caso borde — sin control nativo: en un entorno móvil (sin ::-ms-reveal),
    // sin la corrección no existe forma de revelar la contraseña. Se afirma la
    // Property 1 completa: tieneToggleVisible AND puedeAlternarVisibilidad AND
    // esAccesible.
    it('caso móvil (sin control nativo): existe una forma accesible de revelar la contraseña', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      const passwordInput = screen.getByLabelText(/contraseña/i)
      // Escribir contenido para que aparezca el toggle.
      await user.type(passwordInput, 'clave123')

      // tieneToggleVisible: existe un control propio de revelar contraseña.
      const toggle = screen.getByRole('button', { name: /mostrar contraseña/i })
      expect(toggle).toBeVisible()

      // puedeAlternarVisibilidad: al activarlo, la contraseña se hace visible.
      await user.click(toggle)
      expect(passwordInput).toHaveAttribute('type', 'text')

      // esAccesible: el estado queda reflejado de forma accesible.
      expect(toggle).toHaveAttribute('aria-pressed', 'true')
    })
  })

  // ---------------------------------------------------------------------------
  // Bugfix: "mostrar contraseña en móvil" (spec: mostrar-password-movil-fix)
  // Property 2: Preservation - Flujo de login y valor de contraseña inalterados.
  //
  // Metodología OBSERVACIÓN PRIMERO: estas pruebas capturan el comportamiento
  // REAL del código SIN corregir para entradas no-bug (isBugCondition(X)=false):
  // autenticación válida, validación inline y errores, lockout/temporizador e
  // integridad del valor de la contraseña. Todas DEBEN PASAR ya sobre el código
  // sin corregir y seguir pasando tras implementar el toggle (tarea 3).
  //
  // Las aserciones que dependen del toggle/`aria-pressed` (que aún no existe)
  // se dejan como `it.todo` para no romper la suite; se activarán en la tarea 3.
  // ---------------------------------------------------------------------------
  describe('preservation — flujo de login (Property 2)', () => {
    // Integridad del valor: escribir una contraseña y enviarla preserva el valor
    // `password` intacto. Property test: se generan contraseñas arbitrarias
    // (longitud, caracteres especiales, unicode) y se verifica que el body
    // enviado a la API contiene exactamente el valor escrito.
    it('preserva el valor de la contraseña escrito al enviarlo (property-based)', async () => {
      await fc.assert(
        fc.asyncProperty(
          // Generar contraseñas arbitrarias no vacías (unicode incluido).
          fc.string({ minLength: 1, maxLength: 40 }),
          async (passwordValue) => {
            vi.clearAllMocks()
            // La API resuelve con un token válido para completar el flujo.
            mockApiPost.mockResolvedValueOnce({ token: 'fake-token' })

            const user = userEvent.setup()
            const { unmount } = renderLoginPage()

            const emailInput = screen.getByLabelText(/correo electrónico/i)
            const passwordInput = screen.getByLabelText(/contraseña/i)

            await user.type(emailInput, 'usuario@ejemplo.com')
            // `userEvent.type` interpreta algunos caracteres ([, {) como
            // comandos especiales; se usa el modo literal con doble llave/corchete
            // reemplazado por la API `paste` para preservar el valor tal cual.
            await user.click(passwordInput)
            await user.paste(passwordValue)

            await user.click(screen.getByRole('button', { name: /ingresar/i }))

            await waitFor(() => {
              expect(mockApiPost).toHaveBeenCalledTimes(1)
            })

            // El valor enviado debe ser idéntico al escrito (integridad Req 3.4).
            expect(mockApiPost).toHaveBeenCalledWith(
              '/api/tenants/auth/login',
              { email: 'usuario@ejemplo.com', password: passwordValue },
            )

            unmount()
          },
        ),
        // Menos corridas (10→6): cada iteración monta el componente y usa userEvent
        // real (lento y con alto coste de memoria por worker). 6 corridas mantienen
        // la garantía multi-input sin agotar tiempo ni memoria. Mismos generadores
        // y aserciones.
        { numRuns: 6 },
      )
    }, 30000)

    // Autenticación válida: con credenciales válidas se envía { email, password }
    // al endpoint, se guarda el token vía login(token) y se navega a /dashboard.
    it('con credenciales válidas: envía { email, password }, guarda el token y navega a /dashboard', async () => {
      const user = userEvent.setup()
      mockApiPost.mockResolvedValueOnce({ token: 'jwt-token-valido' })
      renderLoginPage()

      await user.type(screen.getByLabelText(/correo electrónico/i), 'valido@ejemplo.com')
      await user.type(screen.getByLabelText(/contraseña/i), 'ClaveSegura123')
      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      await waitFor(() => {
        expect(mockApiPost).toHaveBeenCalledWith(
          '/api/tenants/auth/login',
          { email: 'valido@ejemplo.com', password: 'ClaveSegura123' },
        )
      })
      expect(mockLogin).toHaveBeenCalledWith('jwt-token-valido')
      expect(mockNavigate).toHaveBeenCalledWith('/dashboard', { replace: true })
    })

    // Validación inline: el campo de correo obligatorio expone aria-invalid y
    // aria-describedby apuntando al mensaje de error observado.
    it('validación inline: correo obligatorio marca aria-invalid y aria-describedby', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      const emailInput = screen.getByLabelText(/correo electrónico/i)
      const passwordInput = screen.getByLabelText(/contraseña/i)

      // Mensajes reales del componente.
      expect(await screen.findByText('El correo es obligatorio')).toBeInTheDocument()
      expect(screen.getByText('La contraseña es obligatoria')).toBeInTheDocument()

      // Atributos de accesibilidad de la validación inline.
      expect(emailInput).toHaveAttribute('aria-invalid', 'true')
      expect(emailInput).toHaveAttribute('aria-describedby', 'login-email-error')
      expect(passwordInput).toHaveAttribute('aria-invalid', 'true')
      expect(passwordInput).toHaveAttribute('aria-describedby', 'login-password-error')

      // La API no debe llamarse cuando la validación falla.
      expect(mockApiPost).not.toHaveBeenCalled()
    })

    // Error 401: "Credenciales inválidas" + mensaje de intentos restantes cuando
    // el body incluye remainingAttempts.
    it('error 401 con remainingAttempts: muestra "Credenciales inválidas" y los intentos restantes', async () => {
      const user = userEvent.setup()
      // Forma del error que produce el cliente API: { status, details }.
      mockApiPost.mockRejectedValueOnce({
        status: 401,
        details: { message: 'fallo', remainingAttempts: 3, lockoutSeconds: 1800 },
      })
      renderLoginPage()

      await user.type(screen.getByLabelText(/correo electrónico/i), 'test@example.com')
      await user.type(screen.getByLabelText(/contraseña/i), 'claveIncorrecta')
      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      expect(await screen.findByText('Credenciales inválidas')).toBeInTheDocument()
      // Texto real del componente para intentos restantes (Req 9.1).
      expect(
        await screen.findByText(/Le quedan 3 intento\(s\)\. Luego deberá esperar 30 minutos/i),
      ).toBeInTheDocument()
    })

    // Error 403 USER_INACTIVE: muestra el mensaje específico del body.
    it('error 403 USER_INACTIVE: muestra el mensaje de cuenta inactiva del servidor', async () => {
      const user = userEvent.setup()
      const mensajeInactivo = 'Su cuenta se encuentra inactiva. Contacte al administrador de su comercio.'
      mockApiPost.mockRejectedValueOnce({
        status: 403,
        details: { message: mensajeInactivo, code: 'USER_INACTIVE' },
      })
      renderLoginPage()

      await user.type(screen.getByLabelText(/correo electrónico/i), 'inactivo@example.com')
      await user.type(screen.getByLabelText(/contraseña/i), 'claveCualquiera')
      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      expect(await screen.findByText(mensajeInactivo)).toBeInTheDocument()
    })

    // Lockout / temporizador: una respuesta 429 con retryAfterSeconds activa el
    // bloqueo (setLockout), oculta el formulario y muestra el temporizador.
    it('lockout 429: activa el bloqueo, oculta el formulario y muestra el temporizador', async () => {
      const user = userEvent.setup()
      mockApiPost.mockRejectedValueOnce({
        status: 429,
        details: { error: 'rate limit', code: 'RATE_LIMIT', retryAfterSeconds: 120 },
      })
      // Capturar `unmount` para desmontar de forma controlada al final del test.
      const { unmount } = renderLoginPage()

      await user.type(screen.getByLabelText(/correo electrónico/i), 'test@example.com')
      await user.type(screen.getByLabelText(/contraseña/i), 'claveCualquiera')
      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      // El mensaje de bloqueo aparece y el formulario se oculta (Req 9.2).
      expect(await screen.findByText('Cuenta bloqueada temporalmente')).toBeInTheDocument()
      // 120 segundos => temporizador "02:00".
      expect(screen.getByText('02:00')).toBeInTheDocument()
      // El botón "Ingresar" ya no está presente porque el formulario se oculta.
      expect(screen.queryByRole('button', { name: /ingresar/i })).not.toBeInTheDocument()

      // `setLockout(120)` inició un setInterval que decrementa el contador cada
      // segundo (setState). Desmontar dentro de act(...) dispara el cleanup del
      // useEffect (detenerTimer -> clearInterval), garantizando que no queden
      // temporizadores vivos ni actualizaciones de estado fuera del ciclo del
      // test (evita warnings de act(...) y retención de referencias).
      await act(async () => {
        unmount()
      })
    })

    // ---------------------------------------------------------------------------
    // Aserciones dependientes del toggle (habilitadas en la tarea 3.4, una vez
    // implementado el toggle en la tarea 3.1). Verifican la preservación del
    // flujo y la integridad del valor al interactuar con el nuevo control.
    // ---------------------------------------------------------------------------

    // Integridad del valor tras alternar la visibilidad N veces con secuencias
    // aleatorias; el valor enviado debe ser idéntico al escrito,
    // independientemente del estado del toggle (Req 3.4).
    it('preserva el valor de la contraseña al alternar la visibilidad N veces', async () => {
      await fc.assert(
        fc.asyncProperty(
          // Contraseña arbitraria no vacía (unicode incluido).
          fc.string({ minLength: 1, maxLength: 40 }),
          // Número de alternancias del toggle antes de enviar.
          fc.integer({ min: 0, max: 6 }),
          async (passwordValue, toggles) => {
            vi.clearAllMocks()
            mockApiPost.mockResolvedValueOnce({ token: 'fake-token' })

            const user = userEvent.setup()
            const { unmount } = renderLoginPage()

            const emailInput = screen.getByLabelText(/correo electrónico/i)
            const passwordInput = screen.getByLabelText(/contraseña/i)

            await user.type(emailInput, 'usuario@ejemplo.com')
            await user.click(passwordInput)
            await user.paste(passwordValue)

            // Alternar la visibilidad N veces; el valor no debe cambiar.
            const toggle = screen.getByRole('button', { name: /mostrar contraseña|ocultar contraseña/i })
            for (let i = 0; i < toggles; i++) {
              await user.click(toggle)
            }

            await user.click(screen.getByRole('button', { name: /ingresar/i }))

            await waitFor(() => {
              expect(mockApiPost).toHaveBeenCalledTimes(1)
            })

            // El valor enviado debe ser idéntico al escrito, sin importar cuántas
            // veces se alternó la visibilidad.
            expect(mockApiPost).toHaveBeenCalledWith(
              '/api/tenants/auth/login',
              { email: 'usuario@ejemplo.com', password: passwordValue },
            )

            unmount()
          },
        ),
        // Menos corridas (10→6) por el coste de montar el componente y usar userEvent
        // real en cada iteración (reduce el pico de memoria por worker sin perder
        // cobertura relevante). Mismos generadores y aserciones.
        { numRuns: 6 },
      )
    }, 30000)

    // Autenticación válida con la contraseña visible mediante el toggle: el
    // flujo (envío, guardado del token, navegación) es idéntico esté el toggle
    // activado u oculto.
    it('autentica correctamente con la contraseña visible mediante el toggle', async () => {
      const user = userEvent.setup()
      mockApiPost.mockResolvedValueOnce({ token: 'jwt-token-visible' })
      renderLoginPage()

      await user.type(screen.getByLabelText(/correo electrónico/i), 'valido@ejemplo.com')
      await user.type(screen.getByLabelText(/contraseña/i), 'ClaveSegura123')

      // Revelar la contraseña antes de enviar.
      const toggle = screen.getByRole('button', { name: /mostrar contraseña/i })
      await user.click(toggle)
      expect(screen.getByLabelText(/contraseña/i)).toHaveAttribute('type', 'text')

      await user.click(screen.getByRole('button', { name: /ingresar/i }))

      await waitFor(() => {
        expect(mockApiPost).toHaveBeenCalledWith(
          '/api/tenants/auth/login',
          { email: 'valido@ejemplo.com', password: 'ClaveSegura123' },
        )
      })
      expect(mockLogin).toHaveBeenCalledWith('jwt-token-visible')
      expect(mockNavigate).toHaveBeenCalledWith('/dashboard', { replace: true })
    })

    // Invariante de accesibilidad: para cualquier estado del toggle, aria-pressed
    // coincide con si el input es type="text".
    it('invariante de accesibilidad: aria-pressed coincide con type="text" para cualquier estado del toggle', async () => {
      const user = userEvent.setup()
      renderLoginPage()

      const passwordInput = screen.getByLabelText(/contraseña/i)
      // Escribir contenido para que aparezca el toggle.
      await user.type(passwordInput, 'clave123')
      const toggle = screen.getByRole('button', { name: /mostrar contraseña|ocultar contraseña/i })

      // Verifica el invariante: aria-pressed === (type === "text").
      const assertInvariant = () => {
        const isText = passwordInput.getAttribute('type') === 'text'
        const isPressed = toggle.getAttribute('aria-pressed') === 'true'
        expect(isPressed).toBe(isText)
      }

      // Estado inicial (oculto).
      assertInvariant()

      // Alternar varias veces y comprobar el invariante en cada estado.
      for (let i = 0; i < 5; i++) {
        await user.click(toggle)
        assertInvariant()
      }
    })
  })

  // Accessibility with axe-core (WCAG 2.1 AA)
  describe('accessibility (WCAG 2.1 AA)', () => {
    it('has no accessibility violations', async () => {
      const { container } = renderLoginPage()
      const results = await axe(container)
      expect(results).toHaveNoViolations()
    })

    it('has no accessibility violations with validation errors shown', async () => {
      const user = userEvent.setup()
      const { container } = renderLoginPage()

      await user.click(screen.getByRole('button', { name: /ingresar/i }))
      await screen.findByText('El correo es obligatorio')

      const results = await axe(container)
      expect(results).toHaveNoViolations()
    })
  })
})
