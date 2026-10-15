<#
.SYNOPSIS
  Instala (o desinstala) el bundle de autocarga de PlanimetriaCogo para el usuario actual.

.DESCRIPTION
  Copia bundle\PlanimetriaCogo.bundle a %APPDATA%\Autodesk\ApplicationPlugins\PlanimetriaCogo.bundle.
  Si el DLL no está en Contents\, intenta tomarlo de PlanimetriaCogo\bin\Release\.
  Civil 3D (cerrado durante la instalación) carga el plugin al escribir PLANIMETRIA, PLANIMETRIACONFIG
  PLANIMETRIAEJE o PLANIMETRIACSV, sin NETLOAD.

.PARAMETER Uninstall
  Elimina el bundle instalado.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File bundle\install.ps1
  powershell -ExecutionPolicy Bypass -File bundle\install.ps1 -Uninstall
#>
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$bundleName = 'PlanimetriaCogo.bundle'
$source = Join-Path $PSScriptRoot $bundleName
$pluginsDir = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$target = Join-Path $pluginsDir $bundleName

if ($Uninstall) {
    if (Test-Path $target) {
        Remove-Item -Recurse -Force $target
        Write-Host "Bundle eliminado de $target"
    } else {
        Write-Host "No había bundle instalado en $target"
    }
    exit 0
}

$dll = Join-Path $source 'Contents\PlanimetriaCogo.dll'
if (-not (Test-Path $dll)) {
    $built = Join-Path $PSScriptRoot '..\PlanimetriaCogo\bin\Release\PlanimetriaCogo.dll'
    if (Test-Path $built) {
        Copy-Item $built (Join-Path $source 'Contents\') -Force
        $pdb = [System.IO.Path]::ChangeExtension($built, '.pdb')
        if (Test-Path $pdb) { Copy-Item $pdb (Join-Path $source 'Contents\') -Force }
        Write-Host "DLL copiado desde $built"
    } else {
        Write-Error "No se encontró PlanimetriaCogo.dll. Compile primero: dotnet build PlanimetriaCogo/PlanimetriaCogo.csproj -c Release"
    }
}

if (Get-Process -Name 'acad' -ErrorAction SilentlyContinue) {
    Write-Warning 'Civil 3D / AutoCAD está abierto. Ciérrelo y vuelva a ejecutar el script para que el bundle se cargue.'
}

New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
Copy-Item -Recurse -Force $source $target
Write-Host "Bundle instalado en $target"
Write-Host 'Abra Civil 3D y escriba PLANIMETRIA.'
