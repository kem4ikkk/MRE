import hashlib
import importlib.util
import json
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
CHART = ROOT / 'runtime/server/Maps/PEI/Chart.png'
HIERARCHY = ROOT / 'runtime/server/Maps/PEI/Level.hierarchy'
spec = importlib.util.spec_from_file_location('locations', ROOT / 'scripts/Export-MrePeiLocations.py')
locations = importlib.util.module_from_spec(spec)
spec.loader.exec_module(locations)

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.headers.get('Host') not in ('127.0.0.1:8765', 'localhost:8765'):
            self.send_error(403, 'Invalid Host'); return
        try:
            if self.path == '/data':
                raw = HIERARCHY.read_bytes()
                parsed = locations.parse(raw.decode('utf-8-sig'))
                data = {'source_sha256': hashlib.sha256(raw).hexdigest(), 'chart_sha256': hashlib.sha256(CHART.read_bytes()).hexdigest(), 'cartography': locations.cartography(parsed), 'locations': locations.extract(parsed)}
                body, mime = json.dumps(data, ensure_ascii=False, allow_nan=False).encode(), 'application/json; charset=utf-8'
            elif self.path == '/chart':
                body, mime = CHART.read_bytes(), 'image/png'
            elif self.path in ('/', '/app.js'):
                body = (HERE / ('index.html' if self.path == '/' else 'app.js')).read_bytes()
                mime = 'text/html; charset=utf-8' if self.path == '/' else 'text/javascript; charset=utf-8'
            else:
                self.send_error(404); return
            self.send_response(200); self.send_header('Content-Type', mime); self.send_header('Content-Length', str(len(body))); self.send_header('Cache-Control', 'no-store'); self.end_headers(); self.wfile.write(body)
        except (OSError, ValueError, TypeError) as exc:
            self.send_error(500, str(exc))

if __name__ == '__main__':
    print('Откройте http://127.0.0.1:8765/ ; остановка Ctrl+C', flush=True)
    HTTPServer(('127.0.0.1', 8765), Handler).serve_forever()
