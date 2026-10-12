# Arquitectura y decisiones

## Selección

| Alternativa | Reutilización real | Decisión |
|---|---|---|
| Avalonia Android | Permite mantener C# y parte de las vistas Avalonia, pero los diálogos, filesystem, plugins y bibliotecas nativas de UABEA requieren cambios. | Viable; no seleccionada para esta primera versión centrada en SAF y controles Android. |
| .NET MAUI | Reutiliza el motor y proporciona UI multiplataforma; añade una abstracción que este proyecto Android no necesita. | Viable, sin ventaja inicial para un único sistema. |
| .NET for Android + C# nativo | Ejecuta AssetsTools.NET directamente y accede a ContentResolver, DocumentsContract, su y controles Material. | **Seleccionada.** |
| Java/Kotlin con motor binario nuevo | Exige reimplementar formatos o mantener un puente/runtime adicional. | No seleccionada. |

No se convierte una aplicación Linux ni se ejecuta UABEA mediante emulación. Se compila el motor C# original para el runtime Android y se adaptan operaciones concretas de UABEA. La interfaz es nueva.

## Capas

- `AssetsTools.Portable`: fuentes originales del motor, sin dependencias de escritorio.
- `Uabea.Core`: documento, prevalidación, directorio, objetos, plantillas, edición, diferencias, reconstrucción y reapertura.
- `Uabea.Storage`: copias mediante streams sin exigir seek al proveedor y verificación de exportaciones.
- `Uabea.Formats`: módulos TextAsset y Texture2D. Son extensiones compiladas; aún no hay cargador dinámico de plugins externos.
- `Uabea.Android`: UI Material 3, SAF, root opcional y proveedor de documentos para autopruebas debug.
- `Uabea.Tests`: fixtures propios e integración completa. UnityPy verifica las salidas de forma independiente.

Cada documento pertenece a un solo trabajador: los lectores internos de AssetsTools.NET comparten streams posicionables y no deben usarse concurrentemente. La UI bloquea operaciones incompatibles mientras está ocupado. Las tareas pesadas usan `Task.Run`.

## Lectura y edición

Se copian los documentos de SAF a archivos privados porque los proveedores pueden entregar streams no posicionables. Se comprueba la cabecera, los rangos de bloques y el directorio antes de enumerar tablas y objetos reales. LZMA se expande a disco; LZ4 utiliza el lector por bloques de upstream. No se cargan referencias externas de rutas controladas por el archivo.

Se usa TypeTree integrado o `classdata.tpk`. Un MonoBehaviour sin TypeTree no tiene edición estructurada: su clase base no describe los campos del script. La edición tipada se bloquea cuando el round-trip del esquema no reproduce exactamente los bytes de entrada. RAW sin esquema solo tiene garantía binaria, no semántica ni de funcionamiento en el juego.

Los reemplazos se mantienen como `IContentReplacer`. Los streams de preview del motor son reutilizables y no deben cerrarse: el port entrega vistas `SegmentStream` para preservar su propiedad.

## Guardado

1. Calcula hashes esperados de todos los objetos, incluidos los modificados.
2. Reconstruye los SerializedFiles y las entradas, luego comprime el bundle si se solicitó.
3. Reabre la salida desde disco con una nueva instancia del motor.
4. Compara directorio, objetos y recursos sin modificar.
5. Sincroniza el archivo y realiza un rename en el mismo directorio privado hacia una ruta nueva.
6. SAF copia la salida validada al documento elegido y verifica SHA-256 releyéndolo.

El guardado privado rechaza cualquier destino existente. SAF usa `ACTION_CREATE_DOCUMENT` y rechaza la URI de entrada. La copia privada original funciona como respaldo de la importación. El proveedor puede impedir garantías atómicas o fallar a mitad de escritura; en ese caso no se declara éxito y se retiene la salida privada. Dos URIs diferentes que representen el mismo documento no se pueden identificar universalmente.

Root es una clase separada. Solo acciones explícitas ejecutan `su`; se comprueba UID 0 al importar, se valida una ruta absoluta y se escapa para shell. La exportación copia un respaldo con nombre único, escribe un temporal, verifica SHA-256 y renombra. No controla procesos de juegos ni modifica anticheat. Las políticas SELinux y el comportamiento de KernelSU/SukiSU aún no están probados.

## Texturas y ARM64

Se usa la decodificación administrada de AssetsTools.NET.Texture y AssetRipper.TextureDecoder. PNG utiliza StbImageSharp/StbImageWriteSharp. Esta ruta no requiere bibliotecas Linux x86-64 y está disponible en ARM64. El módulo upstream contiene wrappers de codificadores nativos que esta aplicación **no invoca**.

La importación implementada genera RGBA32 y mipmaps por promedio en espacio de bytes; no promete filtrado sRGB correcto, preservación de compresión o edición de cubemaps/arrays/platform blobs. ASTC puede codificarse con una futura compilación NDK de astcenc; ETC/PVRTC requieren seleccionar y validar otros codificadores, sus licencias y alineación ARM64. Ninguno de esos encoders nativos está incluido en esta versión. Las rutinas de decodificación adicionales existen en upstream, pero no cuentan como formatos probados hasta tener fixtures dedicados.

## Compatibilidad y seguridad de memoria

Solo UnityFS estándar y SerializedFiles de versión de cabecera 13 a 23 están habilitados. UnityRaw, UnityWeb y UnityArchive se rechazan. La versión Unity del contenido se informa por archivo. Los límites de recursos y prevalidaciones reducen errores, pero no sustituyen fuzzing completo del parser upstream. No hay garantía de abrir cualquier archivo de Unity ni una campaña de rendimiento con bundles de varios GB.

La APK incluye únicamente ARM64; CI también compila x86_64 para ejecutar pruebas en un emulador acelerado. Pasar esas pruebas no equivale a probar físicamente un Xiaomi 15 ni todos los códecs en ARM64.
