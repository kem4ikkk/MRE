// Rebuilds the menu icon sprites from the canonical SVG in this repository.
// Requires the local Playwright install under runtime/ui-review and Microsoft Edge.
const fs = require('node:fs/promises');
const path = require('node:path');
const { createRequire } = require('node:module');

const outputDir = __dirname;
const repoRoot = path.resolve(outputDir, '../../../../..');
const sourcePath = path.join(repoRoot, 'ui/preview/menu-shell/icons.svg');
const localRequire = createRequire(path.join(repoRoot, 'runtime/ui-review/package.json'));
const { chromium } = localRequire('playwright');
const names = ['home', 'help', 'team', 'events', 'stats', 'links', 'settings', 'battlepass', 'rewards', 'reports', 'bell', 'close', 'left', 'right', 'arrow'];

async function main() {
  const source = await fs.readFile(sourcePath, 'utf8');
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 128, height: 128 }, deviceScaleFactor: 1 });
    for (const name of names) {
      const id = `icon-${name}`;
      if (!new RegExp(`<symbol\\s+id="${id}"`).test(source)) throw new Error(`Missing ${id} in ${sourcePath}`);
      const html = `<!doctype html><svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${source}<use href="#${id}"/></svg>`;
      await page.setContent(html);
      await page.locator('body > svg').screenshot({ path: path.join(outputDir, `${name}.png`), omitBackground: true });
    }
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
