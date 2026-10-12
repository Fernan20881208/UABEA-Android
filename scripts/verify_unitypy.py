import json, sys
from pathlib import Path
import UnityPy

root = Path(sys.argv[1])
results = []
for name in ['fixtures/fixture.bundle', 'saved-None.bundle', 'saved-Lz4.bundle', 'saved-Lzma.bundle',
             'standalone.assets', 'texture.bundle', 'material.bundle']:
    env = UnityPy.load(str(root / name))
    assert len(env.objects) == 6, name
    texts = {str(obj.path_id): obj.read_typetree()['m_Script'] for obj in env.objects if obj.type.name == 'TextAsset'}
    assert texts['2'] == 'Keep these exact bytes.\n'
    if name.startswith('saved-'):
        assert texts['1'] == '¡Hola Zaid! 🟣\nA longer replacement with preserved timing-free text.'
    if name == 'standalone.assets':
        assert texts['1'] == 'standalone'
    results.append({'file': name, 'objects': len(env.objects), 'texts': texts})
(root / 'unitypy-independent.json').write_text(json.dumps({'engine': UnityPy.__version__, 'passed': True, 'results': results}, indent=2, ensure_ascii=False))
print('PASS: UnityPy independently reopened seven outputs and verified TextAsset values')
