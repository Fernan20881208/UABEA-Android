import sys, zipfile, subprocess
from pathlib import Path
with zipfile.ZipFile(sys.argv[1]) as apk:
    names = apk.namelist()
    abis = {n.split('/')[1] for n in names if n.startswith('lib/') and n.endswith('.so')}
    assert abis == {'arm64-v8a'}, abis
    assert 'assets/classdata.tpk' in names
    assert not any(n.endswith(('.keystore', '.jks')) for n in names)
    assert not any('libtextureencoder' in n or 'libPVRTexLib' in n for n in names)
    assert any(n.endswith('libmonosgen-2.0.so') for n in names), 'Standalone .NET runtime missing'
print('APK: ARM64 only, standalone runtime, class database, no private keys or desktop texture codecs')
if len(sys.argv) > 2:
    permissions = subprocess.check_output([sys.argv[2], 'dump', 'permissions', sys.argv[1]], text=True)
    forbidden = ['READ_EXTERNAL_STORAGE', 'WRITE_EXTERNAL_STORAGE', 'MANAGE_EXTERNAL_STORAGE',
                 'READ_MEDIA_IMAGES', 'READ_MEDIA_VIDEO', 'READ_MEDIA_AUDIO', 'QUERY_ALL_PACKAGES']
    assert not any('android.permission.' + name in permissions for name in forbidden), permissions
    Path(sys.argv[1]).with_name('APK-PERMISSIONS.txt').write_text(permissions)
    print('Permissions: SAF document grants; no broad storage/media/app-inventory permissions')
