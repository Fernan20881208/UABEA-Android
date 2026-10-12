# UABEA Android

[![Android build and integration tests](https://github.com/Fernan20881208/UABEA-Android/actions/workflows/android.yml/badge.svg)](https://github.com/Fernan20881208/UABEA-Android/actions/workflows/android.yml)

Editor de assets Unity para Android escrito en C#, con el **código fuente real de AssetsTools.NET** y adaptaciones de las operaciones de UABEA. Versión **0.2.0 alpha**. Es un port del motor y de operaciones de edición con una interfaz Android nueva; todavía no tiene todas las funciones de UABEA de escritorio.

Funciona en el teléfono sin Termux, Wine ni un PC. El runtime .NET, la base de tipos y los códecs administrados van dentro de la APK. El acceso habitual utiliza el selector de documentos Android (SAF), sin root ni permisos generales de almacenamiento.

## Descargar e instalar

1. Abre [GitHub Actions](https://github.com/Fernan20881208/UABEA-Android/actions/workflows/android.yml) y selecciona una ejecución **verde**.
2. En **Artifacts**, descarga `UABEA-Android-arm64-debug` y extrae `UABEA-Android-arm64-debug.apk`. El ZIP incluye `SHA256SUMS.txt`.
3. Abre la APK en un Android ARM64 (Android 8 / API 26 o posterior) y autoriza la instalación desde ese gestor de archivos. El objetivo de compilación es Android 16 / API 36.
4. En la aplicación, usa **Herramientas → Abrir demostración** para probar un bundle completo sin archivos de juegos.

Los artefactos de Actions requieren iniciar sesión en GitHub y tienen caducidad. Una ejecución fallida no es una compilación verificada. La APK es debug, autónoma y firmada con una clave de desarrollo generada durante la compilación. Las claves pueden cambiar entre ejecuciones: Android puede exigir desinstalar una APK anterior, lo que borra sus copias privadas. Exporta antes tus archivos. No se publica una clave de firma privada.

## Funciones implementadas

| Área | Implementación actual |
|---|---|
| Bundles | Lectura completa de UnityFS, directorio, bloques, archivos serializados y recursos; entrada/salida sin compresión, LZ4 y LZMA. |
| Archivos | `.assets` independientes; `.bundle` y `.unity3d` si contienen UnityFS compatible. El formato manda, no la extensión. |
| Objetos | Nombre, tipo, PathID, ClassID, tamaño, Container local y versión Unity; búsqueda, filtros y selección múltiple. |
| Extracción | RAW individual, entradas completas y exportación ZIP por lotes; texto y PNG cuando son compatibles. |
| Reemplazo | RAW de objetos, texto, PNG y entradas completas de un bundle. |
| Estructuras | Inspección y edición de escalares mediante JSON tipado; requiere que el esquema reconstruya exactamente el objeto. |
| Material | Números, colores, vectores, nombres serializados y referencias PPtr a shader/textura en propiedades existentes. |
| Shader / ShaderVariantCollection | Inspección estructurada si hay esquema y exportación/reemplazo RAW. Sin descompilador ni compilador ShaderLab. |
| MonoBehaviour | RAW; edición estructurada únicamente con TypeTree propio. |
| Texturas | Vista previa, resolución/formato/mips, PNG; reemplazo explícito como RGBA32 con dimensiones iguales y generación de mipmaps. |
| Hexadecimal | Paginación, comparación antes/después y parches de bytes sujetos a validación. |
| Guardado | Copia nueva, reconstrucción, reapertura y hashes de todos los objetos y recursos; exportación SAF con relectura SHA-256. |
| Android | Material 3 claro/oscuro, tareas fuera de UI, progreso, cancelación, cambios y logs. |
| Root opcional | Solicitud explícita con `su`, importación a copia privada y exportación confirmada con respaldo. Pendiente de probar en KernelSU/SukiSU reales. |

## Uso

- **Abrir** copia el documento a un espacio privado. Un toque selecciona; una pulsación prolongada abre las operaciones de un objeto o entrada.
- **Propiedades** presenta escalares serializados. Cambia `Value`, conserva `Path` y `Type`. Los arrays permiten editar elementos existentes, no añadir o eliminar estructura.
- En Material, `m_Shader` y `m_Texture` contienen `m_FileID`/`m_PathID`. Un nombre arbitrario no crea un shader ni una textura. No se resuelven automáticamente dependencias de otros bundles.
- **Importar PNG** exige las mismas dimensiones y convierte a RGBA32. Puede aumentar mucho el tamaño; no conserva la compresión ASTC/ETC original.
- **Herramientas → Añadir recurso .resS** adjunta un recurso externo seleccionado por el usuario.
- **Guardar como** valida primero una salida privada y después abre el selector de destino. Nunca guarda automáticamente encima de la entrada.
- **Cambios** muestra tamaños y hashes. **Revertir** deshace un objeto. Para deshacer un reemplazo de entrada completa, vuelve a abrir la copia original.
- El acceso root solo se solicita al elegir sus acciones. Para escribir una ruta protegida se muestra el destino exacto y se crea un respaldo si ya existe.

## Límites que debes conocer

- No se ha validado ningún archivo de Free Fire MAX. Un contenedor propietario, cifrado o variante no estándar se rechaza con un error; no se identifica el algoritmo de cifrado ni se evaden protecciones.
- No reconstruye código ShaderLab a partir de shaders GPU compilados. La prueba de Shader usa un objeto serializado sintético, no un shader que renderice.
- Lectura de texturas mediante códecs administrados de upstream. Los fixtures verifican RGBA32; el resto de formatos necesita muestras de prueba. No se incluyen codificadores nativos de escritorio.
- Sin modificación de meshes/audio, generación de scripts MonoBehaviour, recomposición de ASTC/ETC, importación por lotes ni equivalencia completa con plugins de escritorio.
- Límites: entrada 2 GiB, expansión 4 GiB, metadatos 64 MiB, edición estructurada/RAW de objeto 64 MiB, 500 000 objetos, texturas hasta 4 megapíxeles y 4096 por eje. No son garantías de memoria disponible.
- Cancelar se comprueba entre fases/bloques. Algunas rutinas del motor, especialmente descompresión, no se interrumpen inmediatamente.
- Los cambios pendientes viven en memoria. Si Android termina el proceso se pierden; exporta antes de salir. Las copias privadas se conservan, pero no hay aún gestor de recuperación/limpieza.
- SAF depende del proveedor: no todos permiten relectura o escritura atómica. La aplicación informa de fallos y conserva la salida privada validada. Root y uso en un Xiaomi físico requieren validación adicional.

Consulta [arquitectura](docs/ARCHITECTURE.md), [pruebas](docs/TESTING.md), [compilación](docs/BUILD.md), [licencias](THIRD-PARTY-NOTICES.md) e [historial](CHANGELOG.md).

## Procedencia

- [UABEA](https://github.com/nesrak1/UABEA): operaciones TextAsset y mapeo de contenedores adaptadas del código MIT.
- [UABEANext](https://github.com/nesrak1/UABEANext): referencia arquitectónica y `classdata.tpk`.
- [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET): motor incluido como fuente, compresión y serialización reales; módulo Texture incluido como fuente.

Los commits exactos están en [upstream-lock.json](upstream-lock.json). `vendor/` conserva los archivos originales y avisos. Las copias de los dos ZIP aportados por el usuario se inspeccionaron, pero no se redistribuyen: el starter solo detectaba la cabecera y la distribución Ubuntu incluía códecs Linux x86-64 no utilizables como bibliotecas Android.

Proyecto independiente; no está afiliado a nesrak1, Unity ni a desarrolladores de juegos. No incluye assets de juegos, credenciales o claves privadas.

Permisos, diagnóstico en el teléfono y prueba root: [docs/PERMISSIONS.md](docs/PERMISSIONS.md).
