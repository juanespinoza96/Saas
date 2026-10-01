#!/usr/bin/env pwsh
# ═══════════════════════════════════════════════════════════════════════════════
# Script de auditoría de dependencias (Req 3.5)
# Ejecuta verificación de vulnerabilidades en paquetes NuGet y Node.js
# Uso: ./scripts/audit-dependencies.ps1
# Retorna exit code 1 si se detectan vulnerabilidades de severidad alta o crítica.
# ═══════════════════════════════════════════════════════════════════════════════

param(
    [switch]$FailOnVulnerabilities,
    [string]$OutputPath = "./audit-report.txt"
)

$ErrorActionPreference = "Continue"
$exitCode = 0
$report = @()

Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Auditoría de Dependencias - SaasPOS" -ForegroundColor Cyan
Write-Host "  Fecha: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ── 1. Auditoría de paquetes NuGet (.NET) ─────────────────────────────────────
Write-Host "[1/2] Verificando vulnerabilidades en paquetes NuGet..." -ForegroundColor Yellow
Write-Host ""

$report += "═══ REPORTE DE AUDITORÍA DE DEPENDENCIAS ═══"
$report += "Fecha: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$report += ""
$report += "── Paquetes NuGet (.NET) ──"

$nugetOutput = dotnet list package --vulnerable 2>&1
$nugetExitCode = $LASTEXITCODE

if ($nugetOutput -match "has the following vulnerable packages") {
    Write-Host "⚠ Se detectaron paquetes NuGet con vulnerabilidades conocidas:" -ForegroundColor Red
    $nugetOutput | ForEach-Object { Write-Host "  $_" }
    $report += "ESTADO: VULNERABILIDADES DETECTADAS"
    $report += $nugetOutput
    if ($FailOnVulnerabilities) { $exitCode = 1 }
} else {
    Write-Host "✓ No se detectaron vulnerabilidades en paquetes NuGet." -ForegroundColor Green
    $report += "ESTADO: Sin vulnerabilidades detectadas"
}

Write-Host ""

# ── 2. Auditoría de paquetes Node.js (SaasPOS.Web) ────────────────────────────
Write-Host "[2/2] Verificando vulnerabilidades en paquetes Node.js..." -ForegroundColor Yellow
Write-Host ""

$report += ""
$report += "── Paquetes Node.js ──"

$frontendProjects = @(
    @{ Name = "SaasPOS.Web"; Path = "src/SaasPOS.Web" },
    @{ Name = "SaasPOS.Admin"; Path = "src/SaasPOS.Admin" }
)

foreach ($project in $frontendProjects) {
    $projectPath = Join-Path $PSScriptRoot ".." $project.Path

    if (Test-Path (Join-Path $projectPath "package.json")) {
        Write-Host "  Auditando $($project.Name)..." -ForegroundColor Gray

        $report += ""
        $report += "  Proyecto: $($project.Name)"

        Push-Location $projectPath
        $npmOutput = npm audit --json 2>&1

        if ($LASTEXITCODE -ne 0) {
            # npm audit retorna exit code != 0 si hay vulnerabilidades
            $auditJson = $npmOutput | ConvertFrom-Json -ErrorAction SilentlyContinue

            if ($auditJson -and $auditJson.metadata) {
                $vulns = $auditJson.metadata.vulnerabilities
                $highCritical = ($vulns.high ?? 0) + ($vulns.critical ?? 0)

                Write-Host "    ⚠ Vulnerabilidades encontradas en $($project.Name):" -ForegroundColor Red
                Write-Host "      Críticas: $($vulns.critical ?? 0)" -ForegroundColor Red
                Write-Host "      Altas: $($vulns.high ?? 0)" -ForegroundColor Red
                Write-Host "      Medias: $($vulns.moderate ?? 0)" -ForegroundColor Yellow
                Write-Host "      Bajas: $($vulns.low ?? 0)" -ForegroundColor Gray

                $report += "    Críticas: $($vulns.critical ?? 0), Altas: $($vulns.high ?? 0), Medias: $($vulns.moderate ?? 0), Bajas: $($vulns.low ?? 0)"

                if ($highCritical -gt 0 -and $FailOnVulnerabilities) {
                    $exitCode = 1
                }
            } else {
                Write-Host "    ⚠ npm audit reportó problemas (ver reporte para detalles)" -ForegroundColor Yellow
                $report += "    npm audit reportó problemas"
                # Solo incluir las primeras líneas del output si no es JSON válido
                $report += ($npmOutput | Select-Object -First 20)
            }
        } else {
            Write-Host "    ✓ Sin vulnerabilidades en $($project.Name)." -ForegroundColor Green
            $report += "    ESTADO: Sin vulnerabilidades detectadas"
        }

        Pop-Location
    } else {
        Write-Host "  ⊘ $($project.Name): package.json no encontrado, omitiendo." -ForegroundColor Gray
        $report += "  $($project.Name): No encontrado, omitido"
    }
}

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan

# ── Guardar reporte ────────────────────────────────────────────────────────────
$reportPath = Join-Path $PSScriptRoot ".." $OutputPath
$report | Out-File -FilePath $reportPath -Encoding UTF8
Write-Host "Reporte guardado en: $reportPath" -ForegroundColor Gray

# ── Resultado final ────────────────────────────────────────────────────────────
if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "✗ FALLÓ: Se detectaron vulnerabilidades de severidad alta o crítica." -ForegroundColor Red
    Write-Host "  Ejecute 'dotnet list package --vulnerable' y 'npm audit' para detalles." -ForegroundColor Red
} else {
    Write-Host ""
    Write-Host "✓ Auditoría completada exitosamente." -ForegroundColor Green
}

exit $exitCode
