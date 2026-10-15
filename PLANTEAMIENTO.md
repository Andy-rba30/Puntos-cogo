# Planteamiento: plugin de planimetría por lados para Civil 3D 2027

Documento de diseño previo a la programación. Define qué hace el plugin, cómo decide
qué puntos se unen, cómo se estructura el código y qué dudas quedan por resolver.

---

## 1. Problema

Se tiene un levantamiento de puntos COGO con descripciones de campo (`TN`, `VEREDA`,
`CUNETA`, `BORDE`, etc.) tomados a ambos lados de una vía. Se quiere generar la
planimetría automáticamente: unir con líneas los puntos de **igual descripción**, pero
respetando que la vía tiene un lado izquierdo y uno derecho, de modo que los puntos de
un lado **nunca** se unen con los del otro.

Ejemplo del usuario:

| Punto | Descripción | Lado      |
|-------|-------------|-----------|
| 24    | CUNETA      | Izquierda |
| 25    | CUNETA      | Derecha   |
| 26    | CUNETA      | Izquierda |
| 27    | CUNETA      | Derecha   |

Resultado esperado: línea `24 → 26` (cuneta izquierda) y línea `25 → 27` (cuneta
derecha). Nunca `24 → 25`.

## 2. Entradas y salidas

### Entradas

1. **Eje de referencia**: un `Alignment` de Civil 3D (opción recomendada) o, como
   alternativa, una `Polyline` 2D/3D del dibujo.
2. **Puntos COGO**: todos los del dibujo, un `PointGroup`, o una selección en pantalla.
3. **Tabla de códigos** (configurable): qué descripciones procesar, a qué capa van,
   color, tipo de línea, y si el código se divide por lado o no (p. ej. `EJE` es único).
4. **Parámetros**: criterio de orden (número de punto o progresiva), tolerancias de
   corte, distancia máxima al eje, tipo de entidad de salida.

### Salidas

1. Entidades lineales por código y por lado, en su capa:
   `Polyline` 2D (planimetría), `Polyline3d` (con cotas) o `FeatureLine`
   (útil como breakline de superficie). El usuario elige.
2. Reporte en la línea de comandos y opcional en archivo `.txt/.csv`: cuántas líneas se
   crearon, puntos sin código reconocido, puntos descartados por estar fuera del eje,
   segmentos cortados por tolerancia.

## 3. Lógica central

### 3.1 Lectura y normalización de puntos

Por cada `CogoPoint` se arma un objeto `SurveyPoint`:

- `Number`, `Name`, `X`, `Y`, `Z`
- `RawDescription` (código de campo, no la descripción expandida por *description key*)
- `Code`: descripción normalizada (mayúsculas, sin espacios dobles, sin tildes).
  Se admite un *parser* opcional para separar sufijos: `CUNETA I` → código `CUNETA`,
  lado explícito `I`; `CUNETA F` → código `CUNETA`, marca de fin de línea.

### 3.2 Proyección sobre el eje y clasificación de lado

Para cada punto se calcula **progresiva** y **offset** respecto al eje.

- Con `Alignment`: `alignment.StationOffset(x, y, out station, out offset)`.
  Convención de Civil 3D: offset **negativo = izquierda**, **positivo = derecha**,
  mirando en el sentido de progresivas crecientes.
- Con `Polyline`: `Curve.GetClosestPointTo`, parámetro en la curva y signo del producto
  cruz entre la tangente (`GetFirstDerivative`) y el vector eje→punto.

Reglas:

- `|offset| < tolEje` (p. ej. 0.10 m) → lado `Centro`. Solo relevante para códigos
  marcados como "sin lados" (`EJE`, `CL`).
- `|offset| > offsetMax` (p. ej. 60 m) → punto fuera del corredor, se descarta y se
  reporta.
- Punto que no proyecta sobre el eje (antes del inicio o después del fin) → se descarta
  y se reporta, salvo que se active "extrapolar extremos".
- Si la descripción trae lado explícito (`CUNETA I` / `CUNETA D`), **prevalece sobre el
  calculado**, y se reporta la discrepancia si no coinciden.

### 3.3 Riesgo específico: curvas de vuelta (switchbacks)

La imagen de referencia muestra una vía con curvas de vuelta muy cerradas. Ahí un
mismo punto puede estar cerca de **dos tramos** del eje a la vez: a la izquierda de
uno y a la derecha del siguiente. `StationOffset` devuelve la proyección más cercana,
que puede ser la equivocada.

Mitigación propuesta (en dos niveles):

1. **Nivel básico**: proyección más cercana + `offsetMax` ajustado. Suficiente para
   vías normales.
2. **Nivel continuidad** (opcional): para cada punto se obtienen todas las
   proyecciones candidatas (una por tramo del eje dentro de `offsetMax`) y se elige la
   que minimiza el salto de progresiva respecto al punto anterior del mismo código en
   orden numérico. Esto aprovecha que el topógrafo avanza a lo largo de la vía y que la
   numeración sigue ese avance.

### 3.4 Agrupación y orden

Clave de grupo = `(Código, Lado)`. Dentro de cada grupo los puntos se ordenan por:

- **Número de punto** (criterio solicitado por el usuario, refleja el orden de toma), o
- **Progresiva** (refleja el sentido de la vía; útil si la toma fue desordenada).

Ambos disponibles; por defecto número de punto.

### 3.5 Reglas de corte (cuándo NO unir dos puntos consecutivos del mismo grupo)

- Distancia 2D entre consecutivos > `distMax` (p. ej. 50 m).
- Salto de progresiva > `stationGapMax`.
- Marca de fin/inicio en la descripción (`F`, `FIN`, `I`, `INI`, `..`), configurable.
- Sufijo numérico de serie (`CUNETA1`, `CUNETA2`) → series distintas, opcional.

Cada corte produce una nueva polilínea dentro del mismo grupo. Nada se une "a ciegas":
un corte se registra en el reporte con los dos números de punto involucrados.

### 3.6 Ejemplo paso a paso

Puntos 24, 25, 26, 27 con código `CUNETA`:

1. Proyección: 24 → offset −4.1 (Izq), 25 → +3.9 (Der), 26 → −4.0 (Izq), 27 → +4.2 (Der).
2. Grupos: `(CUNETA, Izq) = {24, 26}`, `(CUNETA, Der) = {25, 27}`.
3. Orden por número: ya ordenados.
4. Cortes: distancia 24–26 y 25–27 menor que `distMax`, sin marcas → sin cortes.
5. Salida: polilínea `24-26` en capa `PL-CUNETA-IZQ`, polilínea `25-27` en
   `PL-CUNETA-DER`.

## 4. Arquitectura del código

Un solo proyecto de biblioteca de clases, con la lógica geométrica separada del API de
AutoCAD para poder probarla sin abrir Civil 3D.

```
PlanimetriaCogo/
├─ PlanimetriaCogo.csproj        net8.0-windows, x64, CopyLocal=false en refs de Autodesk
├─ Commands.cs                   [CommandMethod] PLANIMETRIA, PLANIMETRIACONFIG
├─ Models/
│  ├─ SurveyPoint.cs             datos del punto + Station/Offset/Side
│  ├─ Side.cs                    enum Izquierda, Derecha, Centro
│  ├─ CodeRule.cs                código, capa, color, tipo de línea, SplitBySide
│  └─ Settings.cs                tolerancias, orden, tipo de salida
├─ Core/                         SIN dependencias de AutoCAD (testeable)
│  ├─ IAxis.cs                   Project(x, y) → (station, offset, ok)
│  ├─ DescriptionParser.cs       "CUNETA I" → Code=CUNETA, Side=Izq, Flags
│  ├─ SideClassifier.cs          offset → Side con tolerancias
│  ├─ LineGrouper.cs             agrupa por (Code, Side) y ordena
│  └─ BreakRules.cs              aplica reglas de corte → listas de secuencias
├─ Acad/                         CON API de AutoCAD / Civil 3D
│  ├─ CogoPointReader.cs         CogoPoint → SurveyPoint
│  ├─ AlignmentAxis.cs           IAxis sobre Alignment.StationOffset
│  ├─ PolylineAxis.cs            IAxis sobre Curve (polilínea)
│  ├─ GeometryBuilder.cs         secuencias → Polyline / Polyline3d / FeatureLine
│  ├─ LayerManager.cs            crea capas según CodeRule
│  └─ ReportWriter.cs            editor + archivo
├─ Config/
│  └─ ConfigService.cs           JSON en %APPDATA%\PlanimetriaCogo\config.json
└─ PlanimetriaCogo.Tests/        xUnit sobre Core/
```

Referencias de Autodesk (desde la carpeta de instalación de Civil 3D 2027):
`accoremgd.dll`, `acdbmgd.dll`, `acmgd.dll`, `AeccDbMgd.dll`. Versión de .NET a
confirmar contra la instalación real (Civil 3D 2025 y 2026 usan .NET 8).

Distribución: carga manual con `NETLOAD` en la fase de desarrollo; después, carpeta
`bundle` con `PackageContents.xml` en `%APPDATA%\Autodesk\ApplicationPlugins` para
carga automática.

## 5. Flujo del comando `PLANIMETRIA`

1. Pedir el eje: seleccionar un `Alignment` o `Polyline` en pantalla.
2. Pedir los puntos: `Todos` / `Grupo de puntos` / `Selección`.
3. Pedir opciones (o tomarlas de la configuración guardada): orden, tipo de salida,
   tolerancias.
4. Procesar: leer → proyectar → clasificar → agrupar → cortar → dibujar.
5. Mostrar resumen en la línea de comandos y, si se activa, escribir el reporte.

Todo dentro de una sola transacción, con `Undo` de un solo paso.

## 6. Casos borde a contemplar

- Descripción vacía o código no registrado en la tabla → se reporta, no se dibuja
  (opción: dibujar igual en capa genérica `PL-SIN-CODIGO`).
- Números de punto duplicados → se usa el `ObjectId` como desempate y se avisa.
- Puntos con cota 0 → se permiten en 2D; se avisa si la salida es 3D.
- Grupo con un solo punto → no se dibuja línea; se reporta.
- Eje con curvas: `StationOffset` ya trabaja sobre la geometría real, no sobre cuerdas.
- Varios alineamientos en el dibujo → el usuario selecciona uno; el plugin no adivina.
- Description keys activas → se lee siempre `RawDescription` para no depender del
  formato expandido.

## 7. Dudas a resolver antes de programar

1. ¿El eje será siempre un `Alignment` de Civil 3D, o también polilíneas?
2. ¿Salida deseada: polilínea 2D, polilínea 3D, feature line, o varias a la vez?
3. ¿Orden por número de punto es suficiente, o se quiere también por progresiva?
4. ¿Lista de códigos reales del levantamiento? ¿Algunos ya traen el lado o marcas de
   inicio/fin en la descripción?
5. ¿Convención de capas y colores que ya usa el proyecto?
6. ¿Versión exacta de .NET que trae Civil 3D 2027 en la máquina de desarrollo?
7. En las curvas de vuelta, ¿se acepta corrección manual posterior, o se exige el
   modo "continuidad" desde la primera versión?

## 8. Fases de implementación propuestas

| Fase | Alcance |
|------|---------|
| 1    | Comando con `Alignment` + todos los puntos + orden por número + polilíneas 2D + capas por código. |
| 2    | Tabla de códigos en JSON, reglas de corte, descripción con lado/marcas, salida 3D y feature line. |
| 3    | Soporte de `Polyline` como eje, modo "continuidad" para curvas de vuelta, reporte a archivo. |
| 4    | Interfaz WPF (paleta) para configurar códigos y tolerancias, pruebas unitarias del núcleo, bundle de autocarga. |

---

## 9. Decisiones tomadas tras revisar el LandXML del proyecto

Archivo revisado: `PROCESAMIENTO DISEÑO GEOMETRICO.xml` (LandXML 1.2, UTM 17S, Civil 3D 2027).
Contiene 17 014 `CgPoint` y **ningún `Alignment`**: el eje vive solo en el DWG.

### 9.1 Lo que muestran los datos

| Hallazgo | Implicación para el plugin |
|----------|---------------------------|
| Códigos con mayúsculas/minúsculas mezcladas (`tn` 6725, `TN` 1287) | Comparación sin distinguir mayúsculas. |
| Errores de tipeo frecuentes: `cumeta`, `cuenta`, `cunet`, `cunetq`, `cunera`, `vunet`, `vuneta`, `cunetw`, `cumet` | Tabla de **alias** por código + corrección difusa (distancia de edición ≤ 2) con reporte. |
| Abreviaturas: `v`/`ver`/`VRD` (vereda), `b`/`BRD` (borde), `e`/`EJE` (eje), `crr`/`CRC` (carretera), `CNT` (cuneta) | Mismo mecanismo de alias. |
| Códigos de **objeto puntual**: `casa`, `Pt`, `Q`, `PilonA`, `Muro`, `BM`, `PG`, `BZ`, `agua`, `ALC`, `tubo`, `SH`, `GP`, `LOSA` | Tipo `Punto` en la tabla: no se unen, solo se les asigna capa (o se ignoran). |
| 126 puntos sin código y 22 puntos de control con código numérico (`3015396`…) | Se descartan y se listan en el reporte. |
| Patrón de sección `v, e, v` repetido (1150 puntos `e`) | `e` = eje de calzada. Permite **construir el eje desde los puntos** si no hay alineamiento. |
| Vía de ~16 km con desnivel de 338 a 1444 m y muchas curvas de vuelta | El modo "continuidad" (§3.3) pasa a ser necesario desde la fase 1, no opcional. |

### 9.2 Respuestas a las dudas de la sección 7

1. **Eje de referencia: `Alignment` de Civil 3D (recomendado).** Es la entidad que ya sabe
   calcular progresiva y offset con signo, igual que el comando de desfase del programa.
   Se mantienen dos alternativas en el plugin:
   - `Polyline` seleccionada en pantalla.
   - **Generar el eje desde los puntos `e`/`EJE`** ordenados por número, como polilínea
     3D, y opcionalmente convertirlo en `Alignment`. Útil porque el XML ya trae ese dato.

2. **Salida: `Polyline3d` por descripción, cada una en su capa.** Una capa por código
   normalizado y lado, por ejemplo `PL-CUNETA-IZQ`, `PL-CUNETA-DER`, `PL-VEREDA-IZQ`.
   Para códigos sin lados (`EJE`) una sola capa `PL-EJE`. Los objetos puntuales van a
   `PT-CASA`, `PT-MURO`, etc., sin líneas.

3. **Orden: primero el lado (cuadrante), luego el número de punto.** Es decir, la clave
   de agrupación es `(código, lado)` y dentro del grupo se ordena por número. Esto es
   exactamente lo descrito en §3.4; el orden por progresiva queda como opción secundaria
   desactivada por defecto.

4. **Tabla de códigos inicial** (derivada del XML, editable en el JSON de configuración):

   | Código normalizado | Alias detectados | Tipo | Capa base |
   |--------------------|------------------|------|-----------|
   | TN | tn, TN, t, tm | Línea | PL-TN |
   | CUNETA | cuneta, Cuneta, CNT, cumeta, cuenta, cunet, cunetq, cunera, cunetw, cumet, vunet, vuneta | Línea | PL-CUNETA |
   | VEREDA | v, ver, VRD, ve | Línea | PL-VEREDA |
   | BORDE | b, BRD, BD | Línea | PL-BORDE |
   | EJE | e, EJE | Línea (sin lados) | PL-EJE |
   | CARRETERA | crr, Crr, CRC | Línea | PL-CARRETERA |
   | CASA, MURO, PILON, BM, PG, BZ, AGUA, ALC, TUBO, LOSA, SH, GP, Q, PT | — | Punto | PT-<código> |

   Todo alias no listado se intenta resolver por similitud; si no alcanza el umbral, el
   punto se reporta como "código desconocido" y no se dibuja. Nunca se adivina en silencio.

5. **Versión de .NET**: se asume la que el usuario indica (.NET 10 para Civil 3D 2027),
   pero se verifica en la máquina de desarrollo antes de crear el proyecto, revisando el
   *target framework* de `accoremgd.dll` en la carpeta de instalación. El `.csproj` tendrá
   el `TargetFramework` en una sola propiedad para cambiarlo sin tocar el código.

6. **Detección de lado igual que el desfase de Civil 3D**: sí, es el mismo cálculo. El
   método `Alignment.StationOffset` devuelve el offset con signo: negativo = izquierda,
   positivo = derecha mirando hacia progresivas crecientes. Es la misma convención que
   muestra el programa al hacer un desfase. Ese signo es el **primer criterio** de unión
   (define el grupo) y el número de punto el segundo (define el orden dentro del grupo).

### 9.3 Ajustes al flujo y a las fases

- Fase 1 incorpora: normalización por alias, modo continuidad para curvas de vuelta,
  salida `Polyline3d` con capa por código y lado, reporte de códigos desconocidos.
- Se agrega el comando `PLANIMETRIAEJE` para construir el eje desde los puntos `e`.
- Parámetros iniciales sugeridos para esta vía: `offsetMax` 40 m para CUNETA/VEREDA/BORDE,
  80 m para TN; `distMax` 30 m; `tolEje` 0.15 m. Se ajustan en el JSON.
- Con 17 000 puntos el cálculo de `StationOffset` por punto es rápido (segundos), pero
  la creación de entidades se hará en una sola transacción para no degradar el dibujo.
