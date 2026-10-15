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

21. Las 76 pruebas xUnit cubren Core/ y Models/. El proyecto principal (Acad/ y UI/ incluidos)
    se compiló en Linux contra ensamblados *stub* de Autodesk escritos a mano con las firmas
    usadas (`PointLocation`/`StationOffset` con parámetros `ref`, `ShowModalWindow`,
    `CogoPointCollection`, etc.). Eso verifica sintaxis y tipos propios, **no** que las firmas
    coincidan con la API real de Civil 3D 2027: hay que compilar y probar en una máquina con
    Civil 3D instalado.
