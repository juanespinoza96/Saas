#!/bin/bash
# ═══════════════════════════════════════════════════════════════════════════════
# Script de auditoría de dependencias (Req 3.5)
# Ejecuta verificación de vulnerabilidades en paquetes NuGet y Node.js
# Uso: ./scripts/audit-dependencies.sh [--fail-on-vulnerabilities]
# Retorna exit code 1 si se detectan vulnerabilidades de severidad alta o crítica.
# ═══════════════════════════════════════════════════════════════════════════════

set -o pipefail

FAIL_ON_VULNS=false
EXIT_CODE=0
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPORT_FILE="$ROOT_DIR/audit-report.txt"

# Parsear argumentos
for arg in "$@"; do
    case $arg in
        --fail-on-vulnerabilities)
            FAIL_ON_VULNS=true
            ;;
    esac
done

echo "════════════════════════════════════════════════════════════════"
echo "  Auditoría de Dependencias - SaasPOS"
echo "  Fecha: $(date '+%Y-%m-%d %H:%M:%S')"
echo "════════════════════════════════════════════════════════════════"
echo ""

# Iniciar reporte
{
    echo "═══ REPORTE DE AUDITORÍA DE DEPENDENCIAS ═══"
    echo "Fecha: $(date '+%Y-%m-%d %H:%M:%S')"
    echo ""
} > "$REPORT_FILE"

# ── 1. Auditoría de paquetes NuGet (.NET) ─────────────────────────────────────
echo "[1/2] Verificando vulnerabilidades en paquetes NuGet..."
echo ""
echo "── Paquetes NuGet (.NET) ──" >> "$REPORT_FILE"

cd "$ROOT_DIR"
NUGET_OUTPUT=$(dotnet list package --vulnerable 2>&1)

if echo "$NUGET_OUTPUT" | grep -q "has the following vulnerable packages"; then
    echo "⚠ Se detectaron paquetes NuGet con vulnerabilidades conocidas:"
    echo "$NUGET_OUTPUT"
    echo "ESTADO: VULNERABILIDADES DETECTADAS" >> "$REPORT_FILE"
    echo "$NUGET_OUTPUT" >> "$REPORT_FILE"
    if [ "$FAIL_ON_VULNS" = true ]; then
        EXIT_CODE=1
    fi
else
    echo "✓ No se detectaron vulnerabilidades en paquetes NuGet."
    echo "ESTADO: Sin vulnerabilidades detectadas" >> "$REPORT_FILE"
fi

echo ""

# ── 2. Auditoría de paquetes Node.js ──────────────────────────────────────────
echo "[2/2] Verificando vulnerabilidades en paquetes Node.js..."
echo ""
echo "" >> "$REPORT_FILE"
echo "── Paquetes Node.js ──" >> "$REPORT_FILE"

FRONTEND_PROJECTS=("src/SaasPOS.Web" "src/SaasPOS.Admin")

for PROJECT_PATH in "${FRONTEND_PROJECTS[@]}"; do
    PROJECT_NAME=$(basename "$PROJECT_PATH")
    FULL_PATH="$ROOT_DIR/$PROJECT_PATH"

    if [ -f "$FULL_PATH/package.json" ]; then
        echo "  Auditando $PROJECT_NAME..."
        echo "" >> "$REPORT_FILE"
        echo "  Proyecto: $PROJECT_NAME" >> "$REPORT_FILE"

        cd "$FULL_PATH"
        NPM_OUTPUT=$(npm audit --json 2>&1)
        NPM_EXIT=$?

        if [ $NPM_EXIT -ne 0 ]; then
            # Extraer conteos de vulnerabilidades del JSON
            CRITICAL=$(echo "$NPM_OUTPUT" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('metadata',{}).get('vulnerabilities',{}).get('critical',0))" 2>/dev/null || echo "0")
            HIGH=$(echo "$NPM_OUTPUT" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('metadata',{}).get('vulnerabilities',{}).get('high',0))" 2>/dev/null || echo "0")
            MODERATE=$(echo "$NPM_OUTPUT" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('metadata',{}).get('vulnerabilities',{}).get('moderate',0))" 2>/dev/null || echo "0")
            LOW=$(echo "$NPM_OUTPUT" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('metadata',{}).get('vulnerabilities',{}).get('low',0))" 2>/dev/null || echo "0")

            echo "    ⚠ Vulnerabilidades en $PROJECT_NAME: Críticas=$CRITICAL, Altas=$HIGH, Medias=$MODERATE, Bajas=$LOW"
            echo "    Críticas: $CRITICAL, Altas: $HIGH, Medias: $MODERATE, Bajas: $LOW" >> "$REPORT_FILE"

            HIGH_CRITICAL=$((CRITICAL + HIGH))
            if [ "$FAIL_ON_VULNS" = true ] && [ "$HIGH_CRITICAL" -gt 0 ]; then
                EXIT_CODE=1
            fi
        else
            echo "    ✓ Sin vulnerabilidades en $PROJECT_NAME."
            echo "    ESTADO: Sin vulnerabilidades detectadas" >> "$REPORT_FILE"
        fi

        cd "$ROOT_DIR"
    else
        echo "  ⊘ $PROJECT_NAME: package.json no encontrado, omitiendo."
        echo "  $PROJECT_NAME: No encontrado, omitido" >> "$REPORT_FILE"
    fi
done

echo ""
echo "════════════════════════════════════════════════════════════════"
echo "Reporte guardado en: $REPORT_FILE"

# ── Resultado final ────────────────────────────────────────────────────────────
if [ $EXIT_CODE -ne 0 ]; then
    echo ""
    echo "✗ FALLÓ: Se detectaron vulnerabilidades de severidad alta o crítica."
    echo "  Ejecute 'dotnet list package --vulnerable' y 'npm audit' para detalles."
else
    echo ""
    echo "✓ Auditoría completada exitosamente."
fi

exit $EXIT_CODE
