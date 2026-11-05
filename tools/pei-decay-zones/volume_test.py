import copy
import importlib.util
from pathlib import Path

root = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('locations', root / 'scripts/Export-MrePeiLocations.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
parsed = m.parse((root / 'runtime/server/Maps/PEI/Level.hierarchy').read_text(encoding='utf-8-sig'))
volume = m.cartography(parsed)
assert volume['instance_id'] == '8' and volume['scale']['x'] == '1922.711'
for change in ('duplicate', 'rotation', 'zero'):
    candidate = copy.deepcopy(parsed)
    entry = next(x for x in candidate['Items'] if x.get('Type', '').split(',')[0] == 'SDG.Unturned.CartographyVolume')
    if change == 'duplicate': candidate['Items'].append(copy.deepcopy(entry))
    if change == 'rotation': entry['Item']['Rotation']['Y'] = '0.1'
    if change == 'zero': entry['Item']['Scale']['X'] = '0'
    try:
        m.cartography(candidate)
        raise AssertionError(change)
    except ValueError:
        pass
print('PASS: installed volume, duplicate, rotation, zero scale')
