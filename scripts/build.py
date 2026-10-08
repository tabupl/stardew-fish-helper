#!/usr/bin/env python3
"""Build/test/package only. Never starts or connects to Stardew Valley."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--game-path')
args = parser.parse_args()
env = os.environ.copy()
env.update(DOTNET_CLI_HOME=str(ROOT / '.cache/dotnet'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
           DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
           DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true', MSBuildEnableWorkloadResolver='false', DOTNET_CLI_UI_LANGUAGE='en-US')
def run(*cmd):
    result = subprocess.run([args.dotnet, *cmd], cwd=ROOT, env=env, check=False, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(result.stdout)
    result.check_returncode()
    return result.stdout
checks = run('run', '--project', 'tests/FishingPlanner.Tests.csproj', '-c', 'Release', '-p:UseSharedCompilation=false')
cmd = ['build', 'src/FishingPlanner/FishingPlanner.csproj', '-c', 'Release', '--nologo', '-p:UseSharedCompilation=false', '-warnaserror']
if args.game_path:
    cmd.append('-p:GamePath=' + str(Path(args.game_path).resolve()))
build = run(*cmd)
manifest = json.loads((ROOT / 'manifest.json').read_text())
package = ROOT / 'dist/FishingPlanner'
package.mkdir(parents=True, exist_ok=True)
shutil.copy2(ROOT / 'src/FishingPlanner/bin/Release/net6.0/FishingPlanner.dll', package)
for name in ['manifest.json', 'README.md', 'INSTALL.txt', 'LICENSE']:
    shutil.copy2(ROOT / name, package)
files = ['FishingPlanner.dll', 'manifest.json', 'README.md', 'INSTALL.txt', 'LICENSE']
archive = ROOT / 'dist' / f'FishingPlanner-{manifest["Version"]}.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for name in files: z.write(package / name, 'FishingPlanner/' + name)
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    assert len(z.namelist()) == len(files)
    assert all(not n.endswith(('config.json', '.pdb', '.cs')) for n in z.namelist())
hashes = {name: hashlib.sha256((package / name).read_bytes()).hexdigest() for name in files}
(ROOT / 'dist/sha256.json').write_text(json.dumps(hashes, indent=2) + '\n')
(ROOT / 'verification/build.txt').write_text(checks + '\n' + build)
print('Packaged:', archive)
