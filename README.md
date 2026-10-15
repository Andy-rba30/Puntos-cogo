# PlanimetriaCogo

Plugin en C# para **Autodesk Civil 3D 2027** que genera la planimetría de un levantamiento:
une con `Polyline3d` los puntos COGO de igual descripción, separando lado izquierdo y derecho
respecto a un eje (`PLANIMETRIA`), o bien dibuja la planimetría **por bloques** desde un archivo
CSV tal como se tomó en campo, sin eje (`PLANIMETRIACSV`). El diseño está en
[PLANTEAMIENTO.md](PLANTEAMIENTO.md); este repositorio implementa las **cuatro fases** de la
sección 10.4 más el modo por bloques. Las decisiones tomadas durante la implementación están en
[NOTAS_IMPLEMENTACION.md](NOTAS_IMPLEMENTACION.md).

| Fase | Contenido | Estado |
|------|-----------|--------|
| 1 | Comando `PLANIMETRIA` con `Alignment`, ventana de clasificación, lado por `StationOffset`, modo continuidad, `Polyline3d` por capa, reporte en línea de comandos. | Hecho |
| 2 | Reglas de corte configurables (distancia, salto de progresiva, marcas de inicio/fin, series), perfiles JSON, eje desde puntos `EJE`, opción de capa para objetos puntuales, parámetros y catálogo en JSON. | Hecho |
| 3 | Eje por polilínea, reporte a archivo (.txt y .csv), opción "actualizar descripciones en el dibujo". | Hecho |
| 4 | Pruebas unitarias del núcleo (161), bundle de autocarga, pulido de la ventana (filtro, perfiles, catálogo, parámetros, restablecer). | Hecho |
| 5 | Modo por bloques: comando `PLANIMETRIACSV` que lee el CSV de campo y dibuja una `Polyline3d` por cada bloque de puntos consecutivos con la misma descripción, en el orden de toma; la marca `B` abre un bloque nuevo; casas cerradas solo si forman un polígono. | Hecho |

> Todo se compiló y probó sin Civil 3D (ver [Verificación](#verificación)). Falta compilar y probar
> en una máquina con Civil 3D 2027 instalado.

## Estructura

```
PlanimetriaCogo.sln
├─ PlanimetriaCogo/                 biblioteca del plugin (net10.0-windows, x64, WPF)
│  ├─ Commands.cs                   comandos PLANIMETRIA, PLANIMETRIACONFIG, PLANIMETRIAEJE, PLANIMETRIACSV
│  ├─ Models/                       SurveyPoint, Side, CodeKind, CodeRule, Settings
│  ├─ Core/                         lógica sin AutoCAD: normalización, parser de marcas, similitud,
│  │                                eje muestreado, continuidad, agrupación, cortes, motor, reporte,
│  │                                lector CSV y motor por bloques
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
   - *Dibujar*: casilla por fila. Desmarcada, la fila queda fuera del dibujo (sus puntos se cuentan
     como ignorados) pero conserva código, tipo, capa y color para volver a activarla. Se guarda en
     los perfiles.
   - **Buscar** y **Solo sin resolver** filtran la tabla; las columnas se ordenan al pulsar el encabezado.
   - **Sugerir similares**, **Unificar selección** y **Restablecer** (vuelve a la propuesta del catálogo).
   - **Usar nombre como código**: cada fila toma su propia descripción como código destino, **sin marcas** y con
     el resto tal cual (mayúsculas, minúsculas, tildes y espacios respetados: `cunetq B` → `cunetq`, `Cuneta I` →
     `Cuneta`), sin pasar por el catálogo ni por la similitud. Con filas marcadas en *Sel.* o varias filas
     seleccionadas actúa sobre ellas; sin selección, sobre **todas** las filas, en cualquier momento (también
     después de *Sugerir similares*, de cargar un perfil o de editar a mano). Una sola fila resaltada tras un clic
     no cuenta como selección. Las filas en Ignorar solo cambian si se marcan; las descripciones vacías o numéricas
     quedan en Ignorar. *Restablecer* vuelve a la propuesta del catálogo. El parámetro `useDescriptionAsCode` hace
     lo mismo al abrir la ventana con lo que el catálogo no conoce.
   - **Edición en bloque**: seleccione varias filas (clic y Mayús+clic, Ctrl+clic, o la casilla
     *Sel.*) y edite cualquiera de ellas; el código destino, tipo, lado, capa, color o casilla
     *Dibujar* que cambie se copia a todas las seleccionadas. Un clic simple sobre una fila ya seleccionada no deshace la
     selección: abre la celda en edición. Para quedarse con una sola fila, haga clic en una que no
     esté seleccionada. Editar una fila fuera de la selección no toca a las demás.
   - **Perfil** (`Cargar`, `Guardar`, `Abrir archivo…`): guarda la clasificación en
     `%APPDATA%\PlanimetriaCogo\perfiles\<nombre>.json` para reutilizarla en otros DWG. Al cargar,
     coincide por descripción exacta y, si no, por forma normalizada.
   - **Agregar al catálogo**: los códigos nuevos y las descripciones originales (como alias) pasan a
     `catalogo.json`, así se reconocen solos la próxima vez.
   - **Parámetros…**: abre la ventana de parámetros (igual que `PLANIMETRIACONFIG`).
   - **Al dibujar**: casillas *Actualizar descripciones en el dibujo*, *Mover objetos puntuales a su
     capa*, *Guardar reporte en archivo*, *Todas las líneas en una sola capa* (por defecto `Planimetría`
     en color ACI 7, blanco/negro; todas las líneas van ByLayer con ese único color y los objetos
     puntuales siguen en su capa) y, con eje desde puntos, *Conservar el eje generado*. Todas apagadas por defecto; las dos
     primeras piden confirmación.
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
| `distMax` | 30 m | Distancia 2D máxima entre consecutivos; si se supera, corta (en el modo por bloques no corta: solo avisa). 0 = sin límite. |
| `stationGapMax` | 100 m | Salto máximo de progresiva entre consecutivos (corta entre pasadas distintas del eje). 0 = sin límite. |
| `useDescriptionMarks` | `true` | Interpretar marcas en la descripción. |
| `startMarks` / `endMarks` | `INI, INICIO, ..` / `F, FIN` | Marcas de inicio (corte con el anterior) y fin (corte con el siguiente). |
| `blockStartMarks` | `B` | Marca de inicio de bloque (`casa B`): abre un bloque nuevo en el modo por bloques y no forma parte del código (`casa B` y `casa` son la misma fila). En el modo por eje solo se quita del código. |
| `leftMarks` / `rightMarks` | `I, IZQ` / `D, DER` | Lado explícito; prevalece sobre el calculado. |
| `splitSeries` | `false` | `CUNETA1`/`CUNETA2` producen líneas distintas. |
| `movePointObjectsToLayer` | `false` | Mover los puntos de tipo Punto a su capa. |
| `updateDescriptionsInDrawing` | `false` | Reescribir descripciones con el código destino (conserva marcas y serie). |
| `reportToFile` / `reportFolder` | `false` / vacío | Reporte en archivo; vacío = carpeta del DWG. |
| `useDescriptionAsCode` | `false` | Proponer la propia descripción como código destino cuando no está en el catálogo (nada queda sin resolver). |
| `singleLayer` / `singleLayerName` / `singleLayerColor` | `false` / `Planimetría` / `7` | Dibujar todas las líneas en una sola capa, ByLayer, con un solo color (7 = blanco/negro). |
| `lineLayerPrefix`, `pointLayerPrefix`, `leftLayerSuffix`, `rightLayerSuffix` | `PL-`, `PT-`, `-IZQ`, `-DER` | Nomenclatura de capas. |
| `closedBlockCodes` | `CASA` | Modo por bloques: códigos con unión "Bloque cerrado"; el bloque se cierra solo si forma un polígono (4 o más puntos, cierre a escuadra), una fachada queda abierta. |
| `csvColumnOrder` | `pnezd` | Modo por bloques: orden de columnas del archivo la última vez (`pnezd` o `penzd`). |

El catálogo de códigos (§9.2 del planteamiento) vive en `%APPDATA%\PlanimetriaCogo\catalogo.json`,
que se crea con la tabla por defecto la primera vez. Cada entrada tiene `code`, `kind`
(`line`/`point`/`ignore`), `splitBySide`, `colorIndex` y `aliases`.

### `PLANIMETRIAEJE`

Construye el eje desde los puntos `EJE` (y alias) ordenados por número y lo dibuja como
`Polyline3d` en `PL-EJE-GENERADO`, sin crear `Alignment`. Útil para revisarlo, usarlo con la opción
`Polilinea` de `PLANIMETRIA` o convertirlo en alineamiento con las herramientas de Civil 3D.

### `PLANIMETRIACSV` (modo por bloques)

Dibuja la planimetría directamente desde el archivo de puntos de campo, **respetando los bloques, el
orden y la descripción** tal como los tomó el topógrafo. No necesita eje ni calcula lados, y no crea
ni modifica puntos COGO: solo deja `Polyline3d` en sus capas.

1. **Archivo**: CSV o texto con columnas número, norte, este, cota, descripción (`PNEZD`, el formato de
   Civil 3D y de la muestra del proyecto) o número, este, norte, cota, descripción (`PENZD`). El
   separador se detecta solo (coma, punto y coma o tabulador; con punto y coma se admite coma decimal).
   Las filas que no se pueden interpretar (encabezado, vacías) se omiten y se cuentan; sin cota se usa 0.
2. **Orden de columnas** `[PNEZD/PENZD]`; el último usado queda en `config.json`.
3. **Ventana de clasificación** en modo bloques: las mismas filas, perfiles, catálogo, "Sugerir
   similares" y edición en bloque que `PLANIMETRIA`, con estas diferencias:
   - La marca de inicio de bloque (`B`) no crea filas: `casa B` se cuenta en la fila `casa`.
   - Columna **Bloques**: cuántos bloques forma la descripción y el más largo.
   - Columna **Unión** en lugar de *Por lado*: `Por bloque` (una polilínea abierta por bloque, la propuesta
     para todos los códigos, también los que el catálogo tiene como Punto), `Bloque cerrado` (se propone para
     `closedBlockCodes`, `CASA` por defecto; solo se cierra el bloque que forma un polígono) o `Continua` (todos
     los puntos del código en una sola línea, sin respetar bloques; solo si se elige a mano).
   - Para **no dibujar** una descripción: desmarque *Dibujar* o elija tipo *Ignorar*. El tipo *Punto*
     solo cuenta los puntos, nunca los une.
   - **Usar nombre como código** y la casilla *Todas las líneas en una sola capa* funcionan igual que en
     `PLANIMETRIA`.
4. **Dibujar**: el único criterio es el archivo. Se recorren los puntos en su orden y **cada bloque es un
   elemento separado**: una corrida de filas consecutivas con el mismo código destino. El bloque termina en
   cuanto aparece una fila de otro código (aunque no se dibuje) o una marca de inicio de bloque (`casa B` abre
   una casa nueva). Dos bloques nunca se unen. Con "Usar nombre como código" el código es la descripción, así
   que el bloque es exactamente la corrida de filas con la misma descripción. Dentro de un bloque solo cortan
   las marcas de inicio/fin escritas en la descripción; `distMax` no corta: los tramos más largos se dibujan y
   se listan como "Tramos largos dentro de un bloque". Los bloques de un solo punto no se dibujan y se reportan.
   Con `splitSeries`, `CUNETA1` y `CUNETA2` son bloques distintos.
5. Resumen en la línea de comandos (marcas de inicio de bloque leídas, líneas por capa, cuántas cerradas,
   bloques cerrados que quedaron abiertos, tramos largos, bloques de un punto, ignorados, sin resolver) y, si
   está activo, el reporte en archivo. En el `.csv` del reporte la columna `lado` queda vacía y `detalle` dice
   `cerrada` en los polígonos.

Con la muestra corregida `GENERAL_01_PUNTOS_Corregido_Lineas.csv` (16 992 puntos, 4 973 marcas `B`) la lectura
y el cálculo tardan menos de medio segundo: 100 filas en la ventana y unas 3 000 polilíneas (1 451 de `TN`,
1 151 de `CUNETA`, 179 de `CASA`, de las que se cierra 1, el único rectángulo; las demás son fachadas). Un
oráculo independiente verificó todos los códigos del archivo: ningún bloque se pierde ni se mezcla y cada `B`
es el primer vértice de su línea.

## Verificación

- `dotnet test PlanimetriaCogo.Tests`: 219 pruebas en verde (núcleo, configuración, perfiles,
  reporte, lector CSV, motor por bloques y view-models).
- El proyecto principal (Acad/ y UI/ incluidos) se compiló en Linux contra ensamblados *stub* de
  Autodesk escritos a mano con las firmas usadas. Eso verifica sintaxis y tipos propios, **no** que
  las firmas coincidan con la API real de Civil 3D 2027: hay que compilar y probar en una máquina
  con Civil 3D instalado (ver NOTAS_IMPLEMENTACION.md, sección "Pendiente de verificar").
