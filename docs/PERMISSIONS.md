# Almacenamiento y root

## Uso normal

La APK no solicita `READ_EXTERNAL_STORAGE`, `WRITE_EXTERNAL_STORAGE`, `MANAGE_EXTERNAL_STORAGE` ni permisos de medios. Abre documentos con `ACTION_OPEN_DOCUMENT`, conserva una copia privada y exporta mediante `ACTION_CREATE_DOCUMENT`. Cada guardado se relee y compara mediante SHA-256. El manifiesto compilado se audita con `aapt2` en CI; `APK-PERMISSIONS.txt` contiene los permisos reales. El SDK .NET añade `INTERNET` en Debug; la aplicación no implementa un servicio de subida ni telemetría.

SAF concede acceso al documento seleccionado, sin solicitar acceso general. En Android 11+ el selector restringe `Android/data` y `Android/obb`; añadir permisos de almacenamiento antiguos no elimina esa restricción. Referencia: [documentación oficial SAF](https://developer.android.com/training/data-storage/shared/documents-files).

En **Herramientas → Almacenamiento y permisos** se comprueban escritura/lectura privada, hash, espacio libre y lectura del documento SAF actual. No se llama a `su`. Para comprobar escritura en un proveedor real, usa **Guardar como** y reabre su resultado.

## Root opcional

**Herramientas → Comprobar root** ejecuta `su -c 'id -u'`, exige UID 0 y consulta identidad, SELinux y herramientas del sistema. Tiene un límite de 45 segundos y no escribe archivos protegidos. KernelSU/SukiSU debe conceder superusuario al paquete `com.zaid.uabea`. Una denegación no impide usar SAF. No se desactiva SELinux ni se modifican políticas, montajes, permisos de otras aplicaciones o procesos de juegos.

**Abrir con root** lee la ruta indicada por un pipe y copia los bytes al espacio privado con límite de tamaño. **Exportar última copia con root** exige un archivo previamente reconstruido y validado, muestra el destino y pide confirmación. El script:

1. Rechaza rutas no absolutas, saltos de línea, el mismo origen y destinos que sean enlaces simbólicos.
2. Si existe el destino, crea una copia única, verifica su hash y conserva modo, UID/GID y contexto SELinux cuando está disponible. Un fallo de preservación aborta antes de sustituir el destino.
3. Escribe un temporal en el mismo directorio, comprueba SHA-256 y vuelve a comprobar que el original no cambió durante la operación.
4. Renombra el temporal y verifica nuevamente los bytes. La copia anterior se conserva. Al cancelar o fallar antes del renombrado, limpia el temporal.

Las pruebas Linux ejecutan el script real con espacios, apóstrofes y caracteres de shell, permisos POSIX, backup, hash incorrecto, enlace simbólico y destino nuevo. **No prueban la autorización de KernelSU/SukiSU, los contextos SELinux Android ni las particularidades de FUSE/HyperOS.** Se necesita la prueba en el teléfono. No se garantiza la conservación de ACL/xattrs adicionales ni la durabilidad ante corte de energía; no exportes sobre un archivo que otro proceso esté escribiendo. Un archivo nuevo se crea con permisos restrictivos; no se adivinan el propietario ni las reglas de una aplicación destino.

## Prueba manual en Xiaomi

1. Instala la APK y abre **Herramientas → Abrir demostración**. Deben aparecer seis objetos.
2. Ejecuta **Autoprueba Android / SAF** y revisa su informe.
3. Usa **Guardar como**, elige Descargas y abre el archivo creado con **Abrir**.
4. Ejecuta **Almacenamiento y permisos**. Luego **Comprobar root** y concede acceso en el gestor si deseas esa función.
5. Para la primera prueba root utiliza una copia de prueba en Descargas; confirma que la exportación crea `.uabea-backup-*` al sustituir un archivo existente. No necesitas probar primero sobre datos de juegos.

Si falla, comparte el texto de **Registro**, el resultado del diagnóstico y la acción exacta. Los informes permanecen en el teléfono; no se transmiten automáticamente.
