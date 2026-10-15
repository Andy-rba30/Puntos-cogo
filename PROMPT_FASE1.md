# Prompt para Claude Code: fase 1 del plugin de planimetría

Copia el bloque siguiente completo en una sesión nueva de Claude Code abierta sobre el
repositorio `Andy-rba30/Puntos-cogo`, rama `claude/amazing-maxwell-q3iwon`.

---

Trabaja en este repositorio. Lee primero `PLANTEAMIENTO.md` completo: es el diseño
aprobado de un plugin en C# para Autodesk Civil 3D 2027 que genera planimetría uniendo
puntos COGO de igual descripción, separados por lado del eje (izquierda/derecha).
Implementa la **fase 1** tal como la define la sección 10.4 de ese documento. No cambies
las decisiones del planteamiento sin preguntarme; si algo es ambiguo, elige lo más simple
y déjalo anotado en un `NOTAS_IMPLEMENTACION.md`.

## Alcance de la fase 1

1. Comando `PLANIMETRIA` en Civil 3D:
   - Pide seleccionar un `Alignment` (eje de referencia).
   - Pide los puntos: opción `Todos` o selección en pantalla de `CogoPoint`.
   - Abre una ventana WPF de clasificación de descripciones (ver más abajo).
   - Calcula lado con `Alignment.StationOffset`: offset negativo = izquierda,
     positivo = derecha. Puntos que no proyectan o superan `offsetMax` se descartan y
     se reportan.
   - Agrupa por `(código destino, lado)`; códigos marcados "sin lados" (por ejemplo
     `EJE`) forman un solo grupo.
   - Ordena cada grupo por número de punto. Aplica el modo "continuidad" de la
     sección 3.3 para curvas de vuelta: entre varias proyecciones candidatas sobre el
     eje, elige la de menor salto de progresiva respecto al punto anterior del grupo.
   - Dibuja una `Polyline3d` por grupo en la capa indicada (crea la capa si no existe).
   - Escribe un resumen en la línea de comandos: líneas creadas, puntos descartados,
     códigos ignorados y códigos sin resolver.
   - Todo dentro de una sola transacción. No crea grupos de puntos ni alineamientos,
     no modifica ningún `CogoPoint`.

2. Ventana WPF de clasificación (sección 10.2 del planteamiento):
   - Tabla con: descripción original, cantidad, código destino (editable, desplegable),
     tipo (`Línea` / `Punto` / `Ignorar`), capa, color.
   - Botón "Sugerir similares": agrupa sin distinguir mayúsculas, por distancia de
     edición ≤ 2 y por prefijo (`v` → `ver` → `vereda`), y propone un destino común.
   - Botón "Unificar selección": asigna un mismo código destino a las filas marcadas.
   - Propuesta de tipo por defecto: `Punto` para nombres de objeto conocidos (`casa`,
     `muro`, `bm`, `pg`, `bz`, `tubo`, `agua`, `alc`, `losa`, `pilona`), `Línea` para el
     resto.
   - Capa propuesta `PL-<CODIGO>-IZQ` / `PL-<CODIGO>-DER` para líneas, `PT-<CODIGO>` para
     puntos. En esta fase los de tipo `Punto` solo se cuentan, no se dibujan ni se mueven.
   - Contador en vivo: puntos a líneas, a puntos, ignorados, sin resolver.
   - Botones "Dibujar" y "Cancelar".

3. Parámetros con valores por defecto (en un `Settings` simple, aún sin JSON):
   `offsetMax` 40 m, `distMax` 30 m, `tolEje` 0.15 m.

## Estructura y tecnología

- Solución `PlanimetriaCogo.sln` con dos proyectos:
  - `PlanimetriaCogo/` biblioteca de clases, `net10.0-windows`, `x64`, `UseWPF=true`.
    Deja el framework en una propiedad `<TargetFramework>` y las rutas de las DLL de
    Autodesk en una propiedad `<AutodeskDir>` (valor por defecto
    `C:\Program Files\Autodesk\AutoCAD 2027\`). Referencias con `Private=false`:
    `accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll`, `AeccDbMgd.dll`.
  - `PlanimetriaCogo.Tests/` con xUnit, solo sobre la carpeta `Core`.
- Sigue la estructura de carpetas de la sección 4 del planteamiento: `Core/` sin ninguna
  referencia a AutoCAD (usa `double` y estructuras propias, no `Point3d`), `Acad/` con la
  integración, `Models/`, `UI/` para la ventana WPF.
- En `Core/` implementa y prueba: `DescriptionNormalizer` (mayúsculas, tildes, espacios),
  `SimilarityGrouper` (sugerencia de similares), `SideClassifier`, `LineGrouper` (clave
  código+lado, orden por número), `ContinuityResolver` (elección de proyección candidata).
  `IAxis` expone `IEnumerable<Projection> ProjectAll(double x, double y)` para que el
  resolver de continuidad reciba todas las candidatas.
- Para `Alignment` en `Acad/AlignmentAxis.cs`: obtén las candidatas proyectando sobre cada
  entidad del alineamiento (`alignment.Entities`) o, si resulta complejo, usa
  `StationOffset` como única candidata y deja la multi-proyección anotada como pendiente.
- Comentarios y textos de interfaz en español. Nombres de clases y métodos en inglés.
- Incluye un `README.md` con: cómo compilar, cómo cargar con `NETLOAD`, cómo usar el
  comando, y cómo cambiar `AutodeskDir` y `TargetFramework`.

## Validación

- `dotnet build` debe pasar para `PlanimetriaCogo.Tests` y `dotnet test` en verde. El
  proyecto principal puede no compilar en esta máquina si faltan las DLL de Autodesk; en
  ese caso compila al menos `Core/` dentro del proyecto de pruebas y anota en el README
  que la compilación completa requiere Civil 3D 2027 instalado.
- Pruebas mínimas: el ejemplo de la sección 3.6 (puntos 24, 26 izquierda y 25, 27
  derecha producen dos líneas), normalización de `cumeta`/`CNT`/`cuneta` a `CUNETA`,
  sugerencia de similares sobre la lista de alias de la sección 9.2, y un caso de curva de
  vuelta donde la proyección más cercana es la incorrecta y la continuidad la corrige.

## Entrega

Haz commits pequeños con mensajes descriptivos y sube a la rama
`claude/amazing-maxwell-q3iwon`. No abras pull request. Al terminar, resume qué quedó
hecho, qué quedó pendiente de la fase 1 y qué no pudiste verificar por no tener Civil 3D.
