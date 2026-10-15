# Notas de implementación (fase 1)

Decisiones tomadas donde el planteamiento o el prompt dejaban margen. En todos los casos se
eligió lo más simple y se deja aquí para revisarlo.

## Núcleo

1. **Nombres de enumeraciones en inglés.** El planteamiento (§4) menciona `Izquierda, Derecha,
   Centro`; se usó `Side.Left/Right/Center` por coherencia con la regla "clases y métodos en
   inglés". Los textos en español salen de `SideNames.ToSpanish()`.

2. **Continuidad por código, no por (código, lado).** El lado de un punto depende de la
   proyección elegida, así que no se puede agrupar por lado antes de elegirla. Siguiendo §3.3
   ("punto anterior del mismo código en orden numérico"), `ContinuityResolver` recorre los
   puntos de cada código destino ordenados por número; el primero toma la candidata más
   cercana, los siguientes la de menor salto de progresiva (empate: la más cercana). Un punto
   sin candidata válida no rompe la cadena: el siguiente se compara con el último que proyectó.

3. **Candidatas = mínimos locales de distancia a lo largo del eje.** `SampledPolylineAxis`
   recorre la poligonal del eje y toma como candidata cada tramo cuya distancia al punto es
   mínimo local (una por pasada del eje cerca del punto). Un pie en el vértice inicial o final
   de la poligonal cuenta como "no proyecta". Solo se devuelven candidatas dentro del radio de
   búsqueda.

4. **Puntos sobre el eje con código por lados se descartan.** §3.2 dice que `Centro` solo es
   relevante para códigos sin lados. Para un código con lados, un punto con `|offset| < tolEje`
   no tiene lado determinable; en vez de asignarlo por el signo, se descarta y se reporta como
   "sobre el eje (lado indeterminado)". Los códigos sin lados (`EJE`) siempre forman un solo
   grupo (`Side.Center`) independientemente del offset.

5. **`distMax` sí se aplica en la fase 1.** El prompt lo lista entre los parámetros de la fase 1
   y §3.5 pide que nada se una "a ciegas". `BreakRules` corta la secuencia cuando dos puntos
   consecutivos del grupo distan más de `distMax` y registra el corte en el reporte. Por eso un
   grupo puede producir más de una `Polyline3d`. Las demás reglas de corte (salto de progresiva,
   marcas, series) quedan para la fase 2, donde `BreakRules` se ampliará.

6. **Secuencias de un solo punto** no se dibujan y se reportan como "únicos en su grupo" (§6).

7. **Números de punto duplicados**: desempate por `SourceId` (Handle del CogoPoint). No se
   emite aviso específico en esta fase.

## Clasificación y similitud

8. **Carga inicial solo por catálogo.** Al abrir la ventana, cada descripción se resuelve por
   código exacto o alias del catálogo (§9.2). Lo que no está en el catálogo queda "sin resolver"
   hasta que el usuario edite la fila o pulse "Sugerir similares". Así nunca se adivina en
   silencio.

9. **Reglas de "Sugerir similares"** (`SimilarityGrouper`), en orden:
   - normalización (mayúsculas, tildes, espacios);
   - código o alias del catálogo;
   - distancia de edición ≤ 2 respecto a un código del catálogo, solo para textos de 4 o más
     letras (con 3 letras se admite 1 error; con menos, ninguno, porque `v`/`b`/`e` están a
     distancia 1 entre sí);
   - prefijo de exactamente un código del catálogo (`v` → VEREDA; `c` es ambiguo y no se asigna);
   - entre las que siguen sin destino, agrupación por distancia de edición o prefijo de 3+ letras
     (`ver` → `vereda`); los prefijos de 1–2 letras se unen solo si todo lo que empieza así cae
     en un único grupo y ningún código del catálogo compite;
   - el destino de un grupo es el miembro con más puntos, descartando los que son prefijo
     estricto de otro miembro (`v` nunca gana a `vereda`); empate: el más largo;
   - una descripción suelta de 2 o más letras se propone a sí misma como código nuevo
     (`poste` → POSTE); las de una letra, las vacías y las numéricas quedan sin resolver.

10. **Descripciones vacías o numéricas** (`3015396`, puntos de control) se proponen como
    `Ignorar` y aparecen en el reporte de códigos ignorados.

    **Casilla "Dibujar"** (`CodeRule.Draw`, true por defecto): desmarcada, el motor trata la fila
    como ignorada (sus puntos van a `IgnoredCodes`, sin líneas, objetos puntuales ni cambios de
    descripción) pero la fila conserva código, tipo, lado, capa y color, así se reactiva sin
    reconfigurar. Estado "no dibujar"; se guarda en los perfiles y entra en la edición en bloque.

11. **Catálogo en código.** La tabla de §9.2 vive en `CodeCatalog.Default` con un color ACI por
    código (TN 8, CUNETA 4, VEREDA 3, BORDE 1, EJE 2, CARRETERA 5; objetos puntuales 7). Se
    añadieron los alias `CL` → EJE y `pilona` → PILON (el prompt menciona `pilona`). Pasará a
    JSON en la fase 2.

12. **Capa en la tabla = capa base.** La columna *Capa* muestra `PL-CUNETA`; el sufijo
    `-IZQ`/`-DER` se agrega al dibujar cuando la fila tiene *Por lado* marcado. Si varias
    descripciones van al mismo código destino con capas o colores distintos, manda la fila con
    más puntos.

13. **Al cambiar el código destino** de una fila se vuelven a proponer tipo, lados, capa y color
    (desde el catálogo si el código es conocido). Cambiar el tipo recalcula la capa propuesta.

14. **"Unificar selección"** usa las filas marcadas en la columna *Sel.*; si no hay ninguna,
    usa las filas seleccionadas en la tabla.

    **Edición en bloque** (`ClassificationViewModel.PropagateEdit`): al editar una celda, si la fila
    editada está marcada en *Sel.* o seleccionada en la tabla junto con otras, el nuevo valor de esa
    propiedad se copia a las demás (pasando por el *setter*, así un código destino vuelve a proponer
    tipo, capa y color). La ventana anota en `BeginningEdit` la fila, la propiedad y la selección de
    la tabla (el clic que confirma la edición puede cambiarla después) y propaga en el
    `PropertyChanged` de esa fila cuando cambia justo esa propiedad: en las columnas de plantilla
    (Tipo, Código destino) el `DataGrid` no confirma el enlace al cerrar la celda, así que el valor
    llega al elegir en el desplegable o al perder el foco, no en `CellEditEnding`. Abrir una celda y
    salir sin cambiar nada no propaga (no hay `PropertyChanged`). El estado se limpia al cancelar,
    tras confirmar (diferido, por si el enlace llega después) y antes de las acciones de los botones,
    para que un cambio por programa no se tome como edición en bloque. Las marcas de *Sel.* se
    conservan tras propagar para encadenar varios cambios.

    El `DataGrid` deja sola a la fila sobre la que se hace un clic simple aunque forme parte de una
    selección múltiple (`MakeFullRowSelection` sin Ctrl/Mayús limpia el resto antes de editar). Para
    que la edición en bloque funcione con la selección de la tabla, `PreviewMouseLeftButtonDown`
    intercepta ese caso: si la celda pertenece a una fila ya seleccionada, hay más de una seleccionada
    y no hay modificadores, marca el evento como manejado (con el foco en la celda, el manejador de
    clase de `DataGridCell` no toca la selección), fija `CurrentCell` y llama a `BeginEdit()`. La
    casilla *Por lado* se alterna a mano porque el clic ya se consumió; la casilla *Sel.* solo invierte
    la marca. Con la celda ya en edición no interviene, así el desplegable se abre con normalidad.

15. **Dibujar con puntos sin resolver** pide confirmación (se dibujará el resto; los sin
    resolver se reportan). El botón se deshabilita si no hay ningún punto que vaya a líneas.

## Integración con Civil 3D

16. **Multi-proyección sobre `Alignment` por muestreo.** En vez de proyectar analíticamente
    sobre cada `AlignmentEntity` (líneas, arcos y espirales tienen geometrías distintas),
    `AlignmentAxis` muestrea el alineamiento con `PointLocation(station, 0)` cada
    `AxisSampleStep` (1 m) y usa `SampledPolylineAxis` para hallar todas las candidatas. La
    candidata más cercana se reemplaza por el valor exacto de `Alignment.StationOffset`, de modo
    que la proyección normal coincide con la de Civil 3D; las candidatas alternativas llevan el
    error del muestreo (sagita de 1 m de cuerda: ~1 cm con radio 12 m, menos en radios mayores).
    Queda **pendiente** evaluar si conviene la proyección analítica por entidad.

17. **Ecuaciones de progresiva (station equations)** no se consideran: se asume que las
    progresivas de `PointLocation`/`StationOffset` son monótonas entre `StartingStation` y
    `EndingStation`.

18. **Una sola transacción**: lectura de puntos, ventana modal, cálculo y dibujo ocurren dentro
    de la misma transacción; "Cancelar" la descarta sin cambios. La ventana se muestra con
    `Application.ShowModalWindow`.

19. **Selección de puntos** con filtro `AECC_COGO_POINT`; "Todos" usa
    `CivilApplication.ActiveDocument.CogoPoints`.

20. **Rendimiento**: con 17 000 puntos y un eje de 16 km muestreado cada metro, la búsqueda de
    candidatas es O(puntos × tramos) con descarte por caja envolvente; estimado en pocos
    segundos. Si resultara lento, el siguiente paso es un índice espacial por celdas en
    `SampledPolylineAxis`.

## Verificación sin Civil 3D

21. Las 77 pruebas xUnit cubren Core/ y Models/. El proyecto principal (Acad/ y UI/ incluidos)
    se compiló en Linux contra ensamblados *stub* de Autodesk escritos a mano con las firmas
    usadas (`PointLocation`/`StationOffset` con parámetros `ref`, `ShowModalWindow`,
    `CogoPointCollection`, etc.). Eso verifica sintaxis y tipos propios, **no** que las firmas
    coincidan con la API real de Civil 3D 2027: hay que compilar y probar en una máquina con
    Civil 3D instalado.

---

# Notas de implementación (fases 2, 3 y 4)

## Marcas y series en la descripción (§3.1, §3.5)

22. **`I` es lado izquierdo, no inicio.** El planteamiento usa `I` tanto para "lado izquierdo"
    (§3.1: `CUNETA I`) como para "inicio" (§3.5). Se siguió el ejemplo explícito de §3.1:
    `leftMarks = I, IZQ`, `rightMarks = D, DER`, `startMarks = INI, INICIO, ..`, `endMarks = F, FIN`.
    Todas las listas son configurables; la ventana de parámetros rechaza una marca repetida en dos listas.

23. **Solo se interpretan tokens que coinciden con una marca.** `CUNETA ROTA` conserva `CUNETA ROTA`
    como código base para que el usuario lo vea tal cual y decida. Una marca de puntuación (`..`)
    puede ir pegada al código (`CUNETA..`).

24. **Serie = sufijo numérico del primer token** (`CUNETA2`, `CUNETA 2`), solo si el código base tiene
    2 o más letras (`P1` queda como código `P1`). La serie se separa siempre para resolver el código
    base; agrupa en líneas distintas solo con `splitSeries = true`.

25. **Las marcas se resuelven por código base.** Las filas de la ventana siguen siendo una por
    descripción original (`cuneta f` es su propia fila), pero el catálogo y "Sugerir similares"
    comparan el código base, así `cuneta f` propone `CUNETA` sin intervención. Con
    `useDescriptionMarks = false` cada descripción vuelve a ser su propio código (salvo la serie).

26. **Lado explícito prevalece y se reporta.** Si el lado escrito no coincide con el calculado, el
    punto va al grupo escrito y aparece en "Lado explícito distinto del calculado". Un punto sobre el
    eje con lado escrito ya no se descarta: toma el lado escrito.

## Reglas de corte (§3.5)

27. **Orden de evaluación**: marca de fin del anterior → marca de inicio del actual → `distMax` →
    `stationGapMax`. Se registra un solo motivo por corte (el primero que aplica). 0 desactiva las
    reglas numéricas.

28. **`stationGapMax` por defecto 100 m.** Complementa la continuidad en curvas de vuelta: si un punto
    solo tiene candidata en otra pasada del eje (por ejemplo porque la pasada correcta está fuera de
    `offsetMax`), el salto de progresiva corta la línea en vez de unir a ciegas.

29. **Orden por progresiva** (`order = station`) se aplica después de la continuidad, que siempre
    recorre los puntos por número (es el orden de toma, base del criterio de §3.3).

## Configuración, catálogo y perfiles

30. **Archivos en `%APPDATA%\PlanimetriaCogo\`**: `config.json` (parámetros), `catalogo.json`
    (tabla de códigos), `perfiles\*.json` y `reportes\`. Se crean con valores por defecto la primera
    vez. Un archivo dañado no se sobreescribe: se usan los valores por defecto y se avisa en la línea
    de comandos. Propiedades en camelCase y enumeraciones por nombre (`"order": "station"`).

31. **El catálogo deja de vivir en código**: `CodeCatalog.Default` sigue existiendo como semilla y
    respaldo. "Agregar al catálogo" fusiona los códigos nuevos (con tipo, lados y color de la fila) y
    las descripciones originales como alias; nunca quita nada.

32. **Perfiles**: guardan `Original → (código, tipo, lados, capa, color)` de las filas resueltas y de
    las ignoradas. Al cargar, coincidencia por descripción exacta y después por forma normalizada; las
    descripciones sin entrada quedan como estaban y se cuentan en el mensaje de estado.

33. **Las opciones "Al dibujar" se guardan en `config.json`** al pulsar Dibujar (no al cancelar),
    de modo que la elección se recuerda entre sesiones.

## Eje

34. **Eje desde puntos** (§9.2, §10.1): se usan los puntos cuyo código destino es `axisCode` (incluso
    si la fila está en Ignorar), ordenados por número, sin duplicados consecutivos, con progresiva 0 en
    el primero. Vive en memoria; "conservar eje generado" lo dibuja como `Polyline3d` en
    `PL-EJE-GENERADO`. Nunca se crea un `Alignment`. El comando `PLANIMETRIAEJE` (§9.3) hace solo esa
    parte. Ningún filtro de calidad sobre los puntos del eje: si hay saltos, el usuario los ve.

35. **Eje por polilínea**: mismo esquema que `AlignmentAxis` (muestreo + valor exacto para la candidata
    más cercana) con `GetPointAtDist`, `GetClosestPointTo` proyectado en planta y signo por producto
    cruz con `GetFirstDerivative`. Progresiva 0 en el primer vértice; en una `Polyline3d` la distancia
    a lo largo de la curva es 3D (pequeña diferencia con la planta en pendientes fuertes).

## Acciones sobre el dibujo (§10.1, §10.2)

36. **Mover objetos puntuales a su capa** cambia solo `LayerId` del `CogoPoint`. **Actualizar
    descripciones** cambia solo `RawDescription`, reconstruida como código destino + serie + marcas
    (`cumeta f` → `CUNETA F`). Ambas dentro de la transacción del comando (un Deshacer lo revierte
    todo), apagadas por defecto y con confirmación en la ventana.

## Reporte

37. **Un solo formateador** (`ReportFormatter`) para la línea de comandos (listas acotadas a 20) y el
    archivo `.txt` (sin acotar, con detalle de cada línea y de los cambios de descripción). El `.csv`
    usa `;` como separador y punto decimal; una fila por evento (`LINEA`, `CORTE`, `DESCARTADO`,
    `DISCREPANCIA_LADO`, `PUNTO`, `IGNORADO`, `SIN_RESOLVER`). Carpeta: la configurada, si no la del
    DWG, si no `%APPDATA%\PlanimetriaCogo\reportes`.

## Bundle (fase 4)

38. `PackageContents.xml` con `LoadOnCommandInvocation` y los tres comandos. `SeriesMin/Max`
    R25.0–R26.9 por no tener confirmada la serie interna de Civil 3D 2027 (se espera R26.0). En Release
    el `.csproj` copia el DLL a `bundle\PlanimetriaCogo.bundle\Contents\`; `install.ps1` copia el
    bundle a `%APPDATA%\Autodesk\ApplicationPlugins`.

## Pendiente de verificar en una máquina con Civil 3D 2027

39. Las 161 pruebas cubren Core/, Models/, Config/ y los view-models. El plugin completo se compiló en
    Linux contra *stubs* de Autodesk. Lo que **no** se pudo verificar:
    - Firmas reales: `Alignment.PointLocation`/`StationOffset` (parámetros `ref`),
      `CogoPoint.RawDescription` con *setter*, `Curve.GetClosestPointTo(Point3d, Vector3d, bool)`,
      `Application.ShowModalWindow`, `CogoPointCollection` enumerable de `ObjectId`.
    - Comportamiento de la `DataGrid` (edición de celdas, ordenación con `VisibleRows`) y de los
      diálogos de archivo dentro de Civil 3D.
    - Que `PackageContents.xml` sea aceptado por la serie real de Civil 3D 2027.
    - Rendimiento con 17 000 puntos (estimado en segundos; si no, índice espacial en `SampledPolylineAxis`).
