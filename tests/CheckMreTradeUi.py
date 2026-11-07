"""Static cross-file contract checks; Unity build and game interaction remain separate."""
import json
import pathlib
import re

root = pathlib.Path(__file__).resolve().parents[1]
manifest = json.loads((root / 'ui/MreTrade/effect-manifest.json').read_text(encoding='utf-8'))
builder = (root / 'ui/MreTrade/Assets/Editor/MreTradeBuilder.cs').read_text(encoding='utf-8')
script = (root / 'scripts/Build-MreTradeAssets.ps1').read_text(encoding='utf-8')
package_builder = (root / 'tools/trade-workshop/Build-MreTradeWorkshopPackage.ps1').read_text(encoding='utf-8')
assert manifest['effect_guid'] != json.loads((root / 'ui/MreHud/effect-manifest.json').read_text(encoding='utf-8'))['effect_guid']
assert manifest['effect_id'] == 48622 and manifest['effect_key'] == 14822
assert manifest['schema_version'] == 2 and manifest['confirmation_protocol'] == 'nonce-input-v1'
assert manifest['input_fields'] == ['TradeConfirmToken']
assert manifest['workshop_file_id'] == 3834982706
assert re.fullmatch('[0-9a-f]{32}', manifest['effect_guid'])
for value in (str(manifest['effect_id']), str(manifest['effect_key']), manifest['effect_guid']):
    assert value in builder and value in script, value
assert manifest['unity_version'] in builder and manifest['unity_version'] in (root / 'ui/MreTrade/ProjectSettings/ProjectVersion.txt').read_text(encoding='utf-8')
for name in manifest['text_fields'] + manifest['button_names'] + [manifest['visibility_root']]:
    assert f'"{name}"' in builder, name
for name in manifest['input_fields']:
    assert f'"{name}"' in builder and f'"{name}"' in (root / 'src/MreTrade/TradeUi.cs').read_text(encoding='utf-8'), name
for pattern in ['"TradeOwnSlot" + i', '"TradeOwn" + i', '"TradeOther" + i', 'i < 10']:
    assert pattern in builder, pattern
assert 'TradeOtherSlot" + i, content' in builder and 'Button("TradeOtherSlot' not in builder
assert 'text.supportRichText = false' in builder and 'text.raycastTarget = false' in builder
assert 'GraphicRaycaster' in builder and 'mode = Navigation.Mode.None' in builder
assert 'in_game_verified\\\":false' in builder
assert 'LastWriteTimeUtc' in script and 'UnityFS' in script and 'SHA256' in script
assert "$effectManifestPath = Join-Path $unityProject 'effect-manifest.json'" in script
assert '$effect = Get-Content -LiteralPath $effectManifestPath' in script
assert 'Master_Bundle_Override' in script and 'Exclude_From_Master_Bundle' in builder
assert 'MreHud' not in script and 'MreHud' not in builder
assert manifest['bundle_built'] is False and manifest['in_game_verified'] is False
trade_ui = (root / 'src/MreTrade/TradeUi.cs').read_text(encoding='utf-8')
assert 'EffectManager.onEffectTextCommitted += TradeTextCommitted' in trade_ui
assert 'EffectManager.onEffectTextCommitted -= TradeTextCommitted' in trade_ui
assert 'if (name == "TradeConfirm") return;' in trade_ui
assert 's.Acknowledge(id, s.Revision)' not in trade_ui
assert 'Confirm(actor, s, text ?? String.Empty);' in trade_ui
assert 'TradeConfirmToken' in trade_ui and 'GetOrIssueConfirmationTicket(id)' in trade_ui
assert 'SendOnEndEdit' in builder and 'UnityEventTools.AddPersistentListener' in builder
for text in (builder, script, package_builder):
    assert 'nonce-input-v1' in text
    assert 'TradeConfirmToken' in text
assert 'confirmation_protocol' in builder and 'schema_version' in builder
assert 'confirmation_protocol' in package_builder and 'schema_version' in package_builder
assert 'GetPersistentEventCount() != 1' in builder and 'confirmButton.onClick.Invoke()' in builder
assert 'SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime)' in builder
assert 'SetTextWithoutNotify(probe)' in builder
render_ui = trade_ui.split('private void RenderTradeUi', 1)[1].split('private static string SafeLabel', 1)[0]
assert render_ui.index('for (int i = 0; i < 10; i++)') < render_ui.index('"TradeConfirmToken"'), 'nonce must be sent after offer rows'
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
assert 'internal readonly Guid SessionId' in session
assert 'internal bool TryConfirm(ulong id, string nonce, out bool complete)' in session
assert 'ticket.SessionId != SessionId' in session and 'ticket.Revision != Revision' in session
assert 'ticket.Generation != Generation(id)' in session and 'StringComparison.Ordinal' in session
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
