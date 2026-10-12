import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as apk:
    names = apk.namelist()
    abis = {n.split('/')[1] for n in names if n.startswith('lib/') and n.endswith('.so')}
    assert abis == {'arm64-v8a'}, abis
    assert 'assets/classdata.tpk' in names
    assert not any(n.endswith(('.keystore', '.jks')) for n in names)
    assert not any('libtextureencoder' in n or 'libPVRTexLib' in n for n in names)
    assert any(n.endswith('libmonosgen-2.0.so') for n in names), 'Standalone .NET runtime missing'
print('APK: ARM64 only, standalone runtime, class database, no private keys or desktop texture codecs')
