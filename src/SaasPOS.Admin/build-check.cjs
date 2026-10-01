const { execSync } = require('child_process');
try {
  const result = execSync('npx tsc -b', { encoding: 'utf8', stdio: 'pipe', cwd: __dirname });
  console.log(result || 'TSC BUILD OK - No errors');
  process.exit(0);
} catch (e) {
  console.error('TSC BUILD FAILED:');
  console.error(e.stdout || '');
  console.error(e.stderr || '');
  process.exit(1);
}
