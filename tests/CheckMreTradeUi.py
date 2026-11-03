"""Static cross-file contract checks; Unity build and game interaction remain separate."""
import json
import pathlib
import re

root = pathlib.Path(__file__).resolve().parents[1]
manifest = json.loads((root / 'ui/MreTrade/effect-manifest.json').read_text())
builder = (root / 'ui/MreTrade/Assets/Editor/MreTradeBuilder.cs').read_text()
script = (root / 'scripts/Build-MreTradeAssets.ps1').read_text()
assert manifest['effect_guid'] != json.loads((root / 'ui/MreHud/effect-manifest.json').read_text())['effect_guid']
assert manifest['effect_id'] == 48622 and manifest['effect_key'] == 14822
assert re.fullmatch('[0-9a-f]{32}', manifest['effect_guid'])
for value in (str(manifest['effect_id']), str(manifest['effect_key']), manifest['effect_guid']):
    assert value in builder and value in script, value
assert manifest['unity_version'] in builder and manifest['unity_version'] in (root / 'ui/MreTrade/ProjectSettings/ProjectVersion.txt').read_text()
for name in manifest['text_fields'] + manifest['button_names'] + [manifest['visibility_root']]:
    assert f'"{name}"' in builder, name
for pattern in ['"TradeOwnSlot" + i', '"TradeOwn" + i', '"TradeOther" + i', 'i < 10']:
    assert pattern in builder, pattern
assert 'TradeOtherSlot" + i, content' in builder and 'Button("TradeOtherSlot' not in builder
assert 'text.supportRichText = false' in builder and 'text.raycastTarget = false' in builder
assert 'GraphicRaycaster' in builder and 'mode = Navigation.Mode.None' in builder
assert 'in_game_verified\\\":false' in builder
assert 'LastWriteTimeUtc' in script and 'UnityFS' in script and 'SHA256' in script
assert 'Master_Bundle_Override' in script and 'Exclude_From_Master_Bundle' in builder
assert 'MreHud' not in script and 'MreHud' not in builder
assert manifest['bundle_built'] is False and manifest['in_game_verified'] is False
for name in ['index.html', 'style.css', 'app.js']:
    assert (root / 'ui/trade-preview' / name).is_file()
for i in range(10):
    x, y = -510 + (i % 2) * 250, 174 - (i // 2) * 72
    assert -550 <= x and x + 240 <= 550
    assert -375 <= y - 31 and y + 31 <= 375
    other_center = x + 650
    assert -550 <= other_center - 120 and other_center + 120 <= 550
assert 174 - 4 * 72 - 31 > -205 + 42  # grid above hint
print('MRE Trade UI: source contract, separate identifiers, build guards and slot bounds checked. Unity/game not tested.')
