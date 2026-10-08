#!/usr/bin/env python3
"""Install this package only, preserving personal configuration and diagnostics."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import os
import tempfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--destination', required=True, help='Destination Mods/FishingPlanner folder')
args = parser.parse_args()
destination = Path(args.destination).expanduser().resolve()
if destination.name != 'FishingPlanner':
    raise SystemExit('Destination must be the FishingPlanner folder within Mods.')
package = root / 'dist/FishingPlanner'
hashes = json.loads((root / 'dist/sha256.json').read_text())
assert set(hashes) == {'FishingPlanner.dll', 'manifest.json', 'README.md', 'INSTALL.txt', 'LICENSE'}
manifest = destination / 'manifest.json'
if manifest.exists() and json.loads(manifest.read_text(encoding='utf-8-sig')).get('UniqueID') != 'Adam.FishingPlanner':
    raise SystemExit('Refusing to overwrite another mod.')
for name, expected in hashes.items():
    assert hashlib.sha256((package / name).read_bytes()).hexdigest() == expected, name
preserved = {name: (destination / name).read_bytes() for name in ['config.json'] if (destination / name).exists()}
destination.mkdir(parents=True, exist_ok=True)
for name, expected in hashes.items():
    # Replace the directory entry, never truncate a DLL mapped by a running game.
    # The current process keeps its old file; the next launch reads the new one.
    with tempfile.NamedTemporaryFile(dir=destination, prefix='.' + name + '-', delete=False) as pending:
        temporary = Path(pending.name)
    try:
        shutil.copy2(package / name, temporary)
        assert hashlib.sha256(temporary.read_bytes()).hexdigest() == expected, name
        os.replace(temporary, destination / name)
    finally:
        temporary.unlink(missing_ok=True)
    assert hashlib.sha256((destination / name).read_bytes()).hexdigest() == expected, name
for name, previous in preserved.items():
    assert (destination / name).read_bytes() == previous
print(f'Installed and hash-verified {len(hashes)} files in {destination}; existing config preserved.')
