"""Static cross-file contract checks; Unity build and game interaction remain separate."""
import json
import pathlib
import re

root = pathlib.Path(__file__).resolve().parents[1]
manifest = json.loads((root / 'ui/MreTrade/effect-manifest.json').read_text(encoding='utf-8'))
builder = (root / 'ui/MreTrade/Assets/Editor/MreTradeBuilder.cs').read_text(encoding='utf-8')
script = (root / 'scripts/Build-MreTradeAssets.ps1').read_text(encoding='utf-8')
assert manifest['effect_guid'] != json.loads((root / 'ui/MreHud/effect-manifest.json').read_text(encoding='utf-8'))['effect_guid']
assert manifest['effect_id'] == 48622 and manifest['effect_key'] == 14822
assert re.fullmatch('[0-9a-f]{32}', manifest['effect_guid'])
for value in (str(manifest['effect_id']), str(manifest['effect_key']), manifest['effect_guid']):
    assert value in builder and value in script, value
assert manifest['unity_version'] in builder and manifest['unity_version'] in (root / 'ui/MreTrade/ProjectSettings/ProjectVersion.txt').read_text(encoding='utf-8')
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
trade_ui = (root / 'src/MreTrade/TradeUi.cs').read_text(encoding='utf-8')
confirm_handler = re.search(r'if \(name == "TradeConfirm"\)\s*\{(?P<body>.*?)\n\s*\}', trade_ui, re.S)
assert confirm_handler and 's.Acknowledge(id, s.Revision)' in confirm_handler.group('body')
assert 'Confirm(actor, s); return;' in confirm_handler.group('body')
assert '/trade list' not in trade_ui
assert 'RefreshTradeUi(s);' in trade_ui
projection_drag = trade_ui.split('internal void ProjectionDrag', 1)[1]
assert 'Modify(actor, s, "remove"' in projection_drag and 'Modify(actor, s, "offer"' in projection_drag
assert 'editor.Previous = null; RefreshTradeUi(s);' in projection_drag
assert 'перетащите предмет' in trade_ui and 'подтвердите кнопкой' in trade_ui
assert 'if (name == "TradeCancel")' in trade_ui and 'Cancel(s,' in trade_ui
assert '"TradeCancel"' in builder
assert 'перетащите предмет' in builder and 'ПОДТВЕРДИТЬ ОБМЕН' in builder
trade_plugin = (root / 'src/MreTrade/MreTradePlugin.cs').read_text(encoding='utf-8')
assert 'Guid.TryParseExact(args[1], "N"' not in trade_plugin
handle_body = trade_plugin.split('internal void Handle', 1)[1].split('private void Invite', 1)[0]
assert 'action == "list"' not in handle_body
assert 'action == "offer"' not in handle_body and 'action == "remove"' not in handle_body
assert 'action == "confirm"' not in handle_body
assert 'Продолжайте обмен в интерфейсе' in trade_plugin
assert 'UInt64.TryParse(query' in trade_plugin and 'уникальное точное имя игрока' in trade_plugin
cancel_handler = re.search(r'private void Cancel\(TradeSession s, string text\)\s*\{(?P<body>.*?)\n\s*\}', trade_plugin, re.S)
assert cancel_handler and 'Both(s, text)' not in cancel_handler.group('body')
session = (root / 'src/MreTrade/TradeSession.cs').read_text(encoding='utf-8')
assert 'AcknowledgedRevisionA = 0' in session and 'AcknowledgedRevisionB = 0' in session
assert 'AcknowledgedRevision(id) != Revision' in session
assert 'ПОДТВЕРДИТЬ ОБМЕН' in builder
prefab_path = root / 'ui/MreTrade/Assets/MreTrade/Effect.prefab'
assert prefab_path.is_file(), 'generated prefab must be tracked as build input'
prefab = prefab_path.read_text(encoding='utf-8')
def prefab_text(object_name):
    offset = prefab.index(f'm_Name: {object_name}')
    lines = prefab[offset:].splitlines()
    start = next(i for i, line in enumerate(lines) if line.strip().startswith('m_Text:'))
    value = lines[start].split(':', 1)[1].strip()
    i = start + 1
    while not value.endswith('"'):
        value += ' ' + lines[i].strip()
        i += 1
    value = value.replace(r'\xAB', r'\u00AB').replace(r'\xBB', r'\u00BB')
    return json.loads(value)
assert prefab_text('TradeConfirmLabel') == 'ПОДТВЕРДИТЬ ОБМЕН'
assert 'перетащите предмет' in prefab_text('TradeHint').lower()
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
