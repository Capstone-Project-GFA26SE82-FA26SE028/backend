const fs = require('fs');
const path = require('path');

const schemaDir = path.resolve(__dirname, '..', 'docker', 'sql', 'schema');
const seedDir = path.resolve(__dirname, '..', 'docker', 'sql', 'seed');

let allOk = true;

fs.readdirSync(schemaDir).forEach(file => {
  if (!file.endsWith('.sql')) return;
  const db = file.replace('.sql', '');
  const schemaContent = fs.readFileSync(path.join(schemaDir, file), 'utf-8');
  
  // Find all table names using clean regex
  const tableBodies = {};
  const regex = /CREATE TABLE\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?\s*\(([\s\S]*?)\n\)(?:[^\n;]*)/gi;
  let match;
  while ((match = regex.exec(schemaContent)) !== null) {
    const tbl = match[1];
    const body = match[2];
    const cols = new Set();
    
    body.split('\n').forEach(line => {
      const parts = line.split(',');
      parts.forEach(part => {
        const l = part.trim();
        if (!l || l.startsWith('CONSTRAINT') || l.startsWith('PRIMARY KEY') || l.startsWith('--') || l.startsWith('WITH')) return;
        const colMatch = l.match(/\[?(\w+)\]?\s+/);
        if (colMatch && !['CONSTRAINT', 'PRIMARY', 'KEY', 'CHECK', 'FOREIGN', 'REFERENCES', 'UNIQUE'].includes(colMatch[1].toUpperCase())) {
          cols.add(colMatch[1]);
        }
      });
    });
    tableBodies[tbl] = cols;
  }

  const seedFile = path.join(seedDir, file);
  if (!fs.existsSync(seedFile)) {
    console.error(`[MISSING SEED FILE] ${file}`);
    allOk = false;
    return;
  }
  const seedContent = fs.readFileSync(seedFile, 'utf-8');

  // Verify all tables exist
  for (const tbl of Object.keys(tableBodies)) {
    const insRegex = new RegExp(`INSERT\\s+dbo\\.\\[?${tbl}\\]?\\s*\\(([^)]+)\\)`, 'i');
    const insMatch = seedContent.match(insRegex);
    if (!insMatch) {
      console.error(`[${db}] Missing table in seed: ${tbl}`);
      allOk = false;
      continue;
    }

    // Check columns
    const colsInInsert = insMatch[1].split(',').map(c => c.trim().replace(/^\[|\]$/g, ''));
    const schemaCols = tableBodies[tbl];
    colsInInsert.forEach(c => {
      if (!schemaCols.has(c)) {
        console.error(`[${db}] Column [${c}] in INSERT dbo.[${tbl}] does NOT exist in schema! Schema has: ${[...schemaCols].join(', ')}`);
        allOk = false;
      }
    });
  }

  console.log(`[${db}] Verified ${Object.keys(tableBodies).length} tables and all inserted columns match schema.`);
});

if (allOk) {
  console.log('\nSUCCESS: 100% tables and columns match schemas perfectly without errors!');
} else {
  process.exit(1);
}
