const { chromium } = require('/opt/homebrew/lib/node_modules/playwright-mcp-server/node_modules/playwright-core');
const EXE=require('fs').readdirSync(process.env.HOME+'/Library/Caches/ms-playwright/chromium_headless_shell-1243').filter(d=>d.startsWith('chrome-headless')).map(d=>process.env.HOME+'/Library/Caches/ms-playwright/chromium_headless_shell-1243/'+d+'/chrome-headless-shell')[0];
exports.launch=()=>chromium.launch({executablePath:EXE});
