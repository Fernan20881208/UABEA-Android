# Compilar

## Requisitos

- .NET SDK **10.0.401**, fijado por `global.json`.
- Workload `android` (familia Android 36.1).
- JDK 21.
- Android SDK: `platforms;android-36`, `build-tools;36.0.0` y platform-tools.
- Linux, macOS o Windows con esos componentes. Ningún requisito de desarrollo se necesita para ejecutar la APK instalada.

```sh
dotnet workload install android --skip-manifest-update
sdkmanager 'platforms;android-36' 'build-tools;36.0.0'
dotnet build tests/Uabea.Tests -c Release -m:1
dotnet tests/Uabea.Tests/bin/Release/net10.0/Uabea.Tests.dll artifacts/tests
dotnet build src/Uabea.Android -c Debug -t:SignAndroidPackage -m:1 \
  -p:RuntimeIdentifier=android-arm64 \
  -p:AndroidSdkDirectory="$ANDROID_HOME" -p:JavaSdkDirectory="$JAVA_HOME"
```

Resultado:

```text
src/Uabea.Android/bin/Debug/net10.0-android36.0/android-arm64/com.zaid.uabea-Signed.apk
```

`EmbedAssembliesIntoApk=true` evita depender de fast deployment. Se incluye el runtime Mono y los assemblies dentro del paquete. La variante de emulador usa `-p:RuntimeIdentifier=android-x64`.

## Verificación

```sh
python -m pip install UnityPy==1.25.4
python scripts/verify_unitypy.py artifacts/tests
python scripts/verify_apk.py /ruta/UABEA-Android-arm64-debug.apk
"$ANDROID_HOME/build-tools/36.0.0/apksigner" verify --verbose /ruta/UABEA-Android-arm64-debug.apk
"$ANDROID_HOME/build-tools/36.0.0/zipalign" -c -P 16 4 /ruta/UABEA-Android-arm64-debug.apk
```

En emulador Android 16 x86_64 con ADB disponible, después de compilar la variante correspondiente:

```sh
bash scripts/android_selftest.sh
```

El workflow publica APK, SHA-256, archivo de fuentes y reportes. No utiliza una clave release. Para distribución estable será necesario proporcionar una clave mediante secretos o firma externa y establecer una política de conservación; no deben commitearse `.jks`, `.keystore` o contraseñas. Una clave debug generada por un runner efímero no sirve como identidad estable de publicación.
