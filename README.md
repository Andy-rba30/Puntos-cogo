# PlanimetriaCogo

Plugin en C# para **Autodesk Civil 3D 2027** que genera la planimetría de un levantamiento:
une con `Polyline3d` los puntos COGO de igual descripción, separando lado izquierdo y derecho
respecto a un eje. El diseño está en [PLANTEAMIENTO.md](PLANTEAMIENTO.md); este repositorio
implementa las **cuatro fases** de la sección 10.4. Las decisiones tomadas durante la
implementación están en [NOTAS_IMPLEMENTACION.md](NOTAS_IMPLEMENTACION.md).

| Fase | Contenido | Estado |
|------|-----------|--------|
| 1 | Comando `PLANIMETRIA` con `Alignment`, ventana de clasificación, lado por `StationOffset`, modo continuidad, `Polyline3d` por capa, reporte en línea de comandos. | Hecho |
| 2 | Reglas de corte configurables (distancia, salto de progresiva, marcas de inicio/fin, series), perfiles JSON, eje desde puntos `EJE`, opción de capa para objetos puntuales, parámetros y catálogo en JSON. | Hecho |
| 3 | Eje por polilínea, reporte a archivo (.txt y .csv), opción "actualizar descripciones en el dibujo". | Hecho |
| 4 | Pruebas unitarias del núcleo (161), bundle de autocarga, pulido de la ventana (filtro, perfiles, catálogo, parámetros, restablecer). | Hecho |

> Todo se compiló y probó sin Civil 3D (ver [Verificación](#verificación)). Falta compilar y probar
> en una máquina con Civil 3D 2027 instalado.

## Estructura

```
PlanimetriaCogo.sln
├─ PlanimetriaCogo/                 biblioteca del plugin (net10.0-windows, x64, WPF)
│  ├─ Commands.cs                   comandos PLANIMETRIA, PLANIMETRIACONFIG, PLANIMETRIAEJE
│  ├─ Models/                       SurveyPoint, Side, CodeKind, CodeRule, Settings
│  ├─ Core/                         lógica sin AutoCAD: normalización, parser de marcas, similitud,
│  │                                eje muestreado, continuidad, agrupación, cortes, motor, reporte
│  ├─ Config/                       config.json, catalogo.json, perfiles, archivos de reporte
│  ├─ Acad/                         integración: lectura de CogoPoint, ejes (Alignment, polilínea),
│  │                                capas, Polyline3d, modificación opcional de puntos, reporte
│  └─ UI/                           ventanas WPF de clasificación y de parámetros (+ view-models)
├─ PlanimetriaCogo.Tests/           xUnit sobre Core/, Models/, Config/ y los view-models (sin Civil 3D)
└─ bundle/                          PackageContents.xml e install.ps1 para la autocarga
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

La salida queda en `PlanimetriaCogo/bin/Release/PlanimetriaCogo.dll` y, en Release, se copia
también a `bundle/PlanimetriaCogo.bundle/Contents/`. Las DLL de Autodesk no se copian
(`Private=false`): las aporta el propio Civil 3D al cargar el plugin.

> Si en la máquina no están las DLL de Autodesk, `dotnet build` del proyecto principal falla por
> referencias faltantes; el proyecto de pruebas sigue compilando porque enlaza directamente los
> archivos de `Core/`, `Models/`, `Config/` y los view-models, sin referenciar al plugin.

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

## Instalar

### Opción A: bundle de autocarga (recomendada)

1. Compile en Release (el DLL queda en `bundle/PlanimetriaCogo.bundle/Contents/`).
2. Con Civil 3D cerrado, ejecute:

   ```powershell
   powershell -ExecutionPolicy Bypass -File bundle\install.ps1
   ```

   El script copia `PlanimetriaCogo.bundle` a `%APPDATA%\Autodesk\ApplicationPlugins\`. Para
   desinstalar: `install.ps1 -Uninstall`.
3. Abra Civil 3D y escriba `PLANIMETRIA`: el plugin se carga solo la primera vez que se usa un
   comando (`LoadOnCommandInvocation`).

`PackageContents.xml` declara `SeriesMin="R25.0" SeriesMax="R26.9"`. Si Civil 3D 2027 reporta otra
serie interna (variable `ACADVER`), ajuste el rango.

### Opción B: `NETLOAD` manual

1. Abra Civil 3D 2027 y el dibujo con los puntos.
2. Escriba `NETLOAD` y seleccione `PlanimetriaCogo\bin\Release\PlanimetriaCogo.dll`.
3. Si aparece un aviso de seguridad, acepte cargar el ensamblado (o agregue la carpeta a
   `TRUSTEDPATHS`). La carga manual hay que repetirla en cada sesión.

## Comandos

### `PLANIMETRIA`

1. **Eje de referencia** `[Alineamiento/Polilinea/Puntos]`:
   - `Alineamiento`: un `Alignment` de Civil 3D. Progresiva y offset exactos con `StationOffset`.
   - `Polilinea`: una `Polyline`, `Polyline2d` o `Polyline3d`. La progresiva empieza en 0 en el
     primer vértice y crece en el sentido de la polilínea; izquierda/derecha se miran en ese sentido.
   - `Puntos`: el eje se construye en memoria con los puntos cuyo código destino es el del eje
     (`EJE` por defecto, con sus alias `e`, `CL`), ordenados por número. No se crea ningún
     `Alignment`. Con la opción "Conservar el eje generado" se dibuja además como `Polyline3d`
     en `PL-EJE-GENERADO`.
2. **Puntos a procesar**: `Todos` o `Seleccion` (se filtran los CogoPoint).
3. **Ventana de clasificación**, una fila por descripción original:
   - *Código destino* editable con desplegable. Las descripciones con marcas se resuelven por su
     código base: `cuneta f`, `CUNETA I`, `cuneta2` proponen `CUNETA`.
   - *Tipo*: `Línea`, `Punto` (objeto puntual: se cuenta y, si se activa, se mueve de capa) o `Ignorar`.
   - *Por lado*, *Capa* (base; las líneas con lados reciben `-IZQ`/`-DER`), *Color (ACI)*, *Estado*.
   - **Buscar** y **Solo sin resolver** filtran la tabla; las columnas se ordenan al pulsar el encabezado.
   - **Sugerir similares**, **Unificar selección** y **Restablecer** (vuelve a la propuesta del catálogo).
   - **Edición en bloque**: seleccione varias filas (clic y Mayús+clic, Ctrl+clic, o la casilla
     *Sel.*) y edite cualquiera de ellas; el código destino, tipo, lado, capa o color que cambie se
     copia a todas las seleccionadas. Un clic simple sobre una fila ya seleccionada no deshace la
     selección: abre la celda en edición. Para quedarse con una sola fila, haga clic en una que no
     esté seleccionada. Editar una fila fuera de la selección no toca a las demás.
   - **Perfil** (`Cargar`, `Guardar`, `Abrir archivo…`): guarda la clasificación en
     `%APPDATA%\PlanimetriaCogo\perfiles\<nombre>.json` para reutilizarla en otros DWG. Al cargar,
     coincide por descripción exacta y, si no, por forma normalizada.
   - **Agregar al catálogo**: los códigos nuevos y las descripciones originales (como alias) pasan a
     `catalogo.json`, así se reconocen solos la próxima vez.
   - **Parámetros…**: abre la ventana de parámetros (igual que `PLANIMETRIACONFIG`).
   - **Al dibujar**: casillas *Actualizar descripciones en el dibujo*, *Mover objetos puntuales a su
     capa*, *Guardar reporte en archivo* y, con eje desde puntos, *Conservar el eje generado*. Todas
     apagadas por defecto; las dos primeras piden confirmación.
4. **Dibujar**: proyecta cada punto (modo continuidad en curvas de vuelta), determina el lado (el
   escrito en la descripción prevalece y se reporta la discrepancia), agrupa por (código, lado[,
   serie]), ordena (número de punto o progresiva), aplica las reglas de corte y dibuja una
   `Polyline3d` por secuencia en su capa. **Cancelar** no toca el dibujo.
5. Resumen en la línea de comandos y, si está activo, archivos `<dwg>_planimetria_<fecha>.txt` y
   `.csv` (separador `;`) en la carpeta del DWG o en la configurada.

Todo ocurre en una sola transacción: un `Deshacer` revierte el comando completo. Salvo que se
activen las opciones de arriba, el plugin no crea grupos de puntos ni alineamientos y no modifica
ningún CogoPoint.

### `PLANIMETRIACONFIG`

Ventana para editar los parámetros y guardarlos en `%APPDATA%\PlanimetriaCogo\config.json`.
También puede editarse el JSON a mano (se admiten comentarios).

| Parámetro (`config.json`) | Valor | Significado |
|---------------------------|-------|-------------|
| `offsetMax` | 40 m | Distancia máxima al eje; más lejos se descarta y reporta. 0 = sin límite. |
| `axisTolerance` (tolEje) | 0.15 m | Más cerca del eje, lado indeterminado (salvo lado escrito). |
| `axisSampleStep` | 1 m | Paso de muestreo del eje para hallar todas las proyecciones candidatas. |
| `axisCode` / `keepGeneratedAxis` / `generatedAxisLayer` | `EJE` / `false` / `PL-EJE-GENERADO` | Eje desde puntos. |
| `order` | `number` | Orden dentro del grupo: `number` (número de punto) o `station` (progresiva). |
| `distMax` | 30 m | Distancia 2D máxima entre consecutivos; si se supera, corta. 0 = sin límite. |
| `stationGapMax` | 100 m | Salto máximo de progresiva entre consecutivos (corta entre pasadas distintas del eje). 0 = sin límite. |
| `useDescriptionMarks` | `true` | Interpretar marcas en la descripción. |
| `startMarks` / `endMarks` | `INI, INICIO, ..` / `F, FIN` | Marcas de inicio (corte con el anterior) y fin (corte con el siguiente). |
| `leftMarks` / `rightMarks` | `I, IZQ` / `D, DER` | Lado explícito; prevalece sobre el calculado. |
| `splitSeries` | `false` | `CUNETA1`/`CUNETA2` producen líneas distintas. |
| `movePointObjectsToLayer` | `false` | Mover los puntos de tipo Punto a su capa. |
| `updateDescriptionsInDrawing` | `false` | Reescribir descripciones con el código destino (conserva marcas y serie). |
| `reportToFile` / `reportFolder` | `false` / vacío | Reporte en archivo; vacío = carpeta del DWG. |
| `lineLayerPrefix`, `pointLayerPrefix`, `leftLayerSuffix`, `rightLayerSuffix` | `PL-`, `PT-`, `-IZQ`, `-DER` | Nomenclatura de capas. |

El catálogo de códigos (§9.2 del planteamiento) vive en `%APPDATA%\PlanimetriaCogo\catalogo.json`,
que se crea con la tabla por defecto la primera vez. Cada entrada tiene `code`, `kind`
(`line`/`point`/`ignore`), `splitBySide`, `colorIndex` y `aliases`.

### `PLANIMETRIAEJE`

Construye el eje desde los puntos `EJE` (y alias) ordenados por número y lo dibuja como
`Polyline3d` en `PL-EJE-GENERADO`, sin crear `Alignment`. Útil para revisarlo, usarlo con la opción
`Polilinea` de `PLANIMETRIA` o convertirlo en alineamiento con las herramientas de Civil 3D.

## Verificación

- `dotnet test PlanimetriaCogo.Tests`: 161 pruebas en verde (núcleo, configuración, perfiles,
  reporte y view-models).
- El proyecto principal (Acad/ y UI/ incluidos) se compiló en Linux contra ensamblados *stub* de
  Autodesk escritos a mano con las firmas usadas. Eso verifica sintaxis y tipos propios, **no** que
  las firmas coincidan con la API real de Civil 3D 2027: hay que compilar y probar en una máquina
  con Civil 3D instalado (ver NOTAS_IMPLEMENTACION.md, sección "Pendiente de verificar").
