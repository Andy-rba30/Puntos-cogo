# PlanimetriaCogo

Plugin en C# para **Autodesk Civil 3D 2027** que genera la planimetría de un levantamiento:
une con `Polyline3d` los puntos COGO de igual descripción, separando lado izquierdo y derecho
respecto a un `Alignment`. El diseño está en [PLANTEAMIENTO.md](PLANTEAMIENTO.md); este
repositorio implementa la **fase 1** (sección 10.4). Las decisiones tomadas durante la
implementación están en [NOTAS_IMPLEMENTACION.md](NOTAS_IMPLEMENTACION.md).

## Estructura

```
PlanimetriaCogo.sln
├─ PlanimetriaCogo/                 biblioteca del plugin (net10.0-windows, x64, WPF)
│  ├─ Commands.cs                   comando PLANIMETRIA
│  ├─ Models/                       SurveyPoint, Side, CodeKind, CodeRule, Settings
│  ├─ Core/                         lógica sin AutoCAD (normalización, similitud, eje, continuidad, motor)
│  ├─ Acad/                         integración: CogoPointReader, AlignmentAxis, capas, Polyline3d, reporte
│  └─ UI/                           ventana WPF de clasificación de descripciones
└─ PlanimetriaCogo.Tests/           xUnit sobre Core/ y Models/ (no necesita Civil 3D)
```

## Requisitos

- .NET SDK 10 (`dotnet --version` ≥ 10.0).
- Para compilar el plugin completo: Civil 3D 2027 instalado (provee `accoremgd.dll`,
  `acdbmgd.dll`, `acmgd.dll` y `AeccDbMgd.dll`). Las pruebas no lo necesitan.

## Compilar

Pruebas del núcleo (funcionan en cualquier máquina, también Linux/macOS):

```powershell
dotnet test PlanimetriaCogo.Tests
```

Plugin completo (requiere Civil 3D 2027 instalado):

```powershell
dotnet build PlanimetriaCogo/PlanimetriaCogo.csproj -c Release
```

La salida queda en `PlanimetriaCogo/bin/Release/PlanimetriaCogo.dll`. Las DLL de Autodesk
no se copian (`Private=false`): las aporta el propio Civil 3D al cargar el plugin.

> La compilación completa requiere Civil 3D 2027 instalado. Si en la máquina no están las DLL
> de Autodesk, `dotnet build` del proyecto principal falla por referencias faltantes; el
> proyecto de pruebas sigue compilando porque enlaza directamente los archivos de `Core/` y
> `Models/`, sin referenciar al plugin.

### Cambiar `AutodeskDir` y `TargetFramework`

Ambos están en una sola propiedad de `PlanimetriaCogo/PlanimetriaCogo.csproj`:

| Propiedad | Valor por defecto | Para qué |
|-----------|-------------------|----------|
| `TargetFramework` | `net10.0-windows` | Versión de .NET que usa Civil 3D 2027. Si la instalación real usa otra (p. ej. `net8.0-windows`), cámbiala aquí. |
| `AutodeskDir` | `C:\Program Files\Autodesk\AutoCAD 2027\` | Carpeta con `accoremgd.dll`, `acdbmgd.dll` y `acmgd.dll`. |
| `CivilDir` | `$(AutodeskDir)C3D\` si existe, si no `$(AutodeskDir)` | Carpeta con `AeccDbMgd.dll`. |

Se pueden sobreescribir sin editar el archivo:

```powershell
dotnet build PlanimetriaCogo/PlanimetriaCogo.csproj -c Release -p:AutodeskDir="D:\Autodesk\Civil 3D 2027\"
```

Para saber qué versión de .NET usa la instalación, revise `TargetFrameworkAttribute` de
`accoremgd.dll` (por ejemplo con ILSpy o `dotnet-ildasm`).

## Cargar en Civil 3D con NETLOAD

1. Abra Civil 3D 2027 y el dibujo con los puntos y el alineamiento.
2. Escriba `NETLOAD` en la línea de comandos y seleccione
   `PlanimetriaCogo\bin\Release\PlanimetriaCogo.dll`.
3. Si aparece un aviso de seguridad, acepte cargar el ensamblado (o agregue la carpeta a
   `TRUSTEDPATHS`).

La carga manual hay que repetirla en cada sesión. El bundle de autocarga
(`PackageContents.xml`) está previsto para la fase 4.

## Usar el comando `PLANIMETRIA`

1. Escriba `PLANIMETRIA`.
2. **Seleccione el alineamiento** (eje de referencia). Solo acepta `Alignment` de Civil 3D.
3. **Puntos a procesar**: `Todos` (todos los CogoPoint del dibujo) o `Seleccion` (ventana de
   selección en pantalla; se filtran automáticamente los CogoPoint).
4. Se abre la **ventana de clasificación** con una fila por descripción original:
   - *Código destino*: editable con desplegable. Aquí se unifica (`cumeta` → `CUNETA`). Las
     filas vacías se muestran como "(sin resolver)" y no se dibujan.
   - *Tipo*: `Línea` (se une por lado), `Punto` (objeto puntual: en esta fase solo se cuenta) o
     `Ignorar`.
   - *Por lado*: desmarcado para códigos sin lados (`EJE`), que forman un solo grupo.
   - *Capa*: capa base. Las líneas con lados reciben el sufijo `-IZQ` / `-DER` al dibujar
     (`PL-CUNETA` → `PL-CUNETA-IZQ` y `PL-CUNETA-DER`); `EJE` queda en `PL-EJE`.
   - *Color (ACI)*: color de la capa cuando el comando la crea.
   - **Sugerir similares**: agrupa descripciones parecidas (mayúsculas, errores de tipeo con
     distancia de edición ≤ 2, prefijos `v` → `ver` → `vereda`) y propone un destino común.
   - **Unificar selección**: escriba el código y pulse el botón para asignarlo a las filas
     marcadas en la columna *Sel.* (o a las filas seleccionadas en la tabla).
   - El **contador en vivo** muestra puntos a líneas, a puntos, ignorados y sin resolver.
5. **Dibujar**: el comando proyecta cada punto sobre el alineamiento (`StationOffset`,
   offset negativo = izquierda, positivo = derecha), aplica el modo continuidad en curvas de
   vuelta, agrupa por (código, lado), ordena por número de punto y dibuja una `Polyline3d` por
   grupo en su capa (la crea si no existe). **Cancelar** no toca el dibujo.
6. Al terminar, la línea de comandos muestra el resumen: líneas creadas por capa, objetos
   puntuales contados, cortes por distancia, puntos descartados (no proyectan, fuera de
   `offsetMax`, sobre el eje con código por lados, únicos en su grupo), códigos ignorados y
   códigos sin resolver.

Todo ocurre en una sola transacción: un `Deshacer` revierte el comando completo. El plugin no
crea grupos de puntos ni alineamientos y no modifica ningún CogoPoint.

### Parámetros (fase 1: valores por defecto en `Models/Settings.cs`)

| Parámetro | Valor | Significado |
|-----------|-------|-------------|
| `OffsetMax` | 40 m | Distancia máxima al eje; más lejos se descarta y reporta. |
| `DistMax` | 30 m | Distancia máxima entre puntos consecutivos de un grupo; si se supera, se corta la línea. |
| `AxisTolerance` (tolEje) | 0.15 m | Puntos más cerca del eje se consideran "sobre el eje". |
| `AxisSampleStep` | 1 m | Paso de muestreo del alineamiento para hallar todas las proyecciones candidatas. |

La configuración en JSON llega en la fase 2.
