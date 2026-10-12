# Pruebas y alcance de la verificación

Las pruebas generan un **UnityFS completo**, no una cabecera ficticia. Incluye metadatos, tablas, TypeTrees y seis objetos: dos TextAsset, Material, Texture2D RGBA32 de 2×2, Shader sintético y AssetBundle con Container. También hay una entrada `.resS`. El código generador y sus datos son propios, bajo MIT; no proceden de juegos.

## Suite de motor

`tests/Uabea.Tests` ejecuta 12 casos y devuelve un código distinto de cero si falla alguno:

1. Generación del fixture completo.
2. Lectura de directorio, objetos, nombres, versiones y Container.
3. Edición TextAsset → guardado sin compresión → reapertura → bytes intactos en objetos no editados.
4. El mismo ciclo con LZ4.
5. El mismo ciclo con LZMA.
6. Edición/guardado de `.assets` independiente.
7. Material: float, color y PPtr; inspección de estructura Shader.
8. PNG: dimensiones, orientación, reemplazo RGBA32 y píxeles al reabrir.
9. Reemplazo de recurso y entrada serializada completa.
10. JSON tipado, RAW incompatible rechazado y reversión.
11. Protección del original, destino existente rechazado y limpieza al cancelar.
12. Archivos truncados, corruptos, opacos y formatos antiguos no implementados.

Resultado local inicial: **12 aprobadas, 0 fallidas**. Se detectó y corrigió un error real de propiedad de streams de replacers durante esta suite. Los reportes de cada ejecución están en el artefacto `engine-test-results`: `results.json`, `junit.xml`, fixtures y archivos reconstruidos.

`scripts/verify_unitypy.py` abre siete salidas con **UnityPy 1.25.4**, un parser independiente. Verifica seis objetos y los textos esperados, incluyendo persistencia y conservación del segundo TextAsset. Resultado local inicial: aprobado. Esto reduce la posibilidad de que el mismo motor genere y acepte un formato erróneo.

## Android y SAF

El job `android-saf` compila exactamente las mismas fuentes para x86_64 e instala la APK en un emulador Android 16 / API 36. Ejecuta dentro de Android:

- ContentResolver y un DocumentsProvider real para importar un documento.
- Lectura de objetos, cambio de TextAsset y guardado LZ4.
- Creación de documento, exportación SAF, relectura de hash y reapertura de contenido.
- Exportación de textura PNG y rechazo de sobrescritura de la URI original.

El proveedor de prueba solo se compila en debug y está protegido por el permiso de sistema `MANAGE_DOCUMENTS`, como exige Android para DocumentsProvider. No tiene filtro de descubrimiento en el selector ni expone archivos del usuario. El resultado está en `android-saf-test-results/selftest.json`, con API, ABI y lista de comprobaciones. **Solo un job verde confirma que esa ejecución pasó.** El test automatizado no pulsa el selector gráfico de documentos ni verifica proveedores de nube; el informe lo declara explícitamente.

## APK ARM64

El job `apk` exige que pase la suite del motor, compila y firma la APK autónoma, comprueba firma, ZIP alignment con páginas de 16 KiB, ABI ARM64 exclusiva, runtime integrado y ausencia de claves/encoders de escritorio. Resultado local inicial: compilación y comprobaciones aprobadas. El resultado de GitHub Actions se consulta en la ejecución, no se infiere del resultado local.

## Pendiente de validar en hardware

- Instalación y sesiones prolongadas en Xiaomi 15, HyperOS 3, Android 16.
- Selector gráfico: abrir desde Descargas; editar; crear un documento nuevo; cerrar y volver a abrir ese documento.
- Proveedores externos con streams no posicionables, fallo de relectura, falta de espacio y cancelación durante exportación.
- KernelSU/SukiSU, concesión/denegación de `su`, backup y exportación protegida.
- Materiales/shaders/texturas de distintas versiones de Unity y fixtures dedicados para ASTC, ETC, Crunch y mipmaps.
- Archivos de Free Fire MAX aportados legítimamente por el usuario: no se probó ninguno ni se afirma compatibilidad.
- Memoria/rendimiento con archivos grandes y fuzzing del parser.

Reabrir un bundle y preservar bytes no demuestra que una modificación semántica sea válida para un juego. Un shader de otra plataforma o un PPtr inexistente puede pasar integridad binaria y seguir siendo inválido para Unity.
