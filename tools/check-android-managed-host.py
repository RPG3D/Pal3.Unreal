"""Device regression through game commands, with reversible preference/save snapshots."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--adb', default=shutil.which('adb') or str(Path(os.environ.get('ANDROID_HOME', '')) / 'platform-tools' / ('adb.exe' if os.name == 'nt' else 'adb')))
parser.add_argument('--serial', required=True)
parser.add_argument('--package', default='com.YourCompany.SharpDemo')
parser.add_argument('--project-name', default='SharpDemo')
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--apk', type=Path)
parser.add_argument('--scenes', action='store_true', help='Exercise all variant-specific scene shortcuts, restoring autosaves afterward.')
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
saved = f'/sdcard/Android/data/{args.package}/files/UnrealGame/{args.project_name}/{args.project_name}/Saved'
channel = saved + '/ManagedHostQA/' + uuid.uuid4().hex
preference = saved + '/GameHost/SelectedGame.txt'


def adb(*values, check=True):
    result = subprocess.run([args.adb, '-s', args.serial, *map(str, values)], capture_output=True)
    if check and result.returncode:
        raise RuntimeError(result.stderr.decode(errors='replace') + result.stdout.decode(errors='replace'))
    return result


def read(path):
    if adb('shell', 'test', '-f', path, check=False).returncode:
        return None
    return adb('exec-out', 'cat', path).stdout


def write(path, contents):
    local = args.output / ('upload-' + uuid.uuid4().hex)
    local.write_bytes(contents)
    try:
        adb('shell', 'mkdir', '-p', path.rsplit('/', 1)[0])
        adb('push', local, path + '.qa-tmp')
        adb('shell', 'mv', path + '.qa-tmp', path)
    finally:
        local.unlink(missing_ok=True)


def save_names():
    result = []
    for directory in ['Saves', 'PAL3A/Saves']:
        listing = adb('shell', 'ls', saved + '/' + directory, check=False)
        if listing.returncode:
            continue
        for name in listing.stdout.decode().splitlines():
            if re.fullmatch(r'slot_\d+_v\d+\.txt', name):
                result.append(directory + '/' + name)
    return result


def log():
    return adb('logcat', '-d', '-v', 'threadtime', check=False).stdout.decode(errors='replace')


def launch(parameters=''):
    adb('shell', 'am', 'force-stop', args.package)
    adb('logcat', '-c')
    command = ['shell', 'am', 'start', '-n', args.package + '/com.epicgames.unreal.SplashActivity']
    if parameters:
        if any(character in parameters for character in '\n\r"'):
            raise ValueError('Invalid launch parameters')
        command += ['--es', 'cmdline', '"' + parameters + '"']
    adb(*command)


def request(game, command, argument='', timeout=30):
    root = channel + '/' + game
    session = json.loads(read(root + '/session.json'))['session']
    ident = uuid.uuid4().hex
    remote = root + '/' + session + '/' + ident
    deadline = datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(seconds=timeout)
    payload = json.dumps({'command': command, 'argument': argument, 'deadlineUtc': deadline.isoformat()}).encode()
    write(remote + '.request.json', payload)
    try:
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            response = read(remote + '.response.json')
            if response:
                value = json.loads(response)
                if not value.get('ok'):
                    raise RuntimeError(value.get('error'))
                return value['result']
            if not adb('shell', 'pidof', args.package, check=False).stdout.strip():
                raise RuntimeError('Process exited while waiting for ' + command)
            time.sleep(.2)
        raise TimeoutError(command)
    finally:
        adb('shell', 'rm', '-f', remote + '.request.json', remote + '.response.json', check=False)


def wait(game, state, timeout=100):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        if read(channel + '/' + game + '/session.json'):
            status = request(game, 'status')
            if status.get('bootError') or status.get('sceneLoadError'):
                raise RuntimeError(str(status))
            if status.get('state') == state and not status.get('loadingScene'):
                return status
        text = log()
        if 'Fatal error:' in text or 'GameHost: Error:' in text:
            raise RuntimeError('Engine reported a fatal/startup error')
        time.sleep(.5)
    raise TimeoutError(state)


if args.apk:
    adb('install', '-r', args.apk.resolve())
adb('shell', 'am', 'force-stop', args.package)
old_choice = read(preference)
if old_choice is not None:
    (args.output / 'SelectedGame.before.txt').write_bytes(old_choice)
backups = {name: read(saved + '/' + name) for name in save_names()}
for name, contents in backups.items():
    target = args.output / 'Before' / name
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(contents)
results = []
try:
    launch()
    end = time.monotonic() + 90
    while time.monotonic() < end:
        text = log()
        if '[RuntimeWidgetLayout] widget added' in text:
            break
        if 'Fatal error:' in text or 'GameHost: Error:' in text:
            raise RuntimeError('Game-list startup failed')
        time.sleep(.5)
    else:
        raise TimeoutError('Game list')
    assert 'Full boot starting' not in text, 'A game session started before selection'
    (args.output / 'GameList.png').write_bytes(adb('exec-out', 'screencap', '-p').stdout)
    for game, expected in [('pal3', ('q01', 'yn09a')), ('pal3a', ('q02', 'qn08y'))]:
        adb('shell', 'am', 'force-stop', args.package)
        write(preference, game.encode())
        launch('-GameHostAutostart -Pal3AutomationDir=' + channel + '/' + game)
        status = wait(game, 'Title')
        assert (status['city'].lower(), status['scene'].lower()) == expected, status
        for iteration in range(5):
            assert request(game, 'gc')['scheduled']
            time.sleep(.7)
            assert request(game, 'status')['state'] == 'Title'
        assert len(request(game, 'slots')) == 100
        scenes = request(game, 'list')['scenes']
        visited = []
        if args.scenes:
            expected_scenes = {
                'pal3': [('q01', 'b', 195, 212), ('q01', 'xa', 215, 47), ('q02', 'q02', 70, 209), ('q03', 'q03', 325, 165)],
                'pal3a': [('q01', 'q01', 190, 20), ('q02', 'qh', 91, 114), ('q02', 'hh', 342, 137)]
            }[game]
            assert len(scenes) == len(expected_scenes), scenes
            for number, label in enumerate(scenes, 1):
                request(game, 'scene', str(number))
                status = wait(game, 'Gameplay')
                assert status['playerHasControl'], status
                player = status['player']
                actual = (status['city'].lower(), status['scene'].lower(), player['tileX'], player['tileY'])
                assert actual == expected_scenes[number - 1] and player['layer'] == 0, status
                visited.append({'label': label, 'city': status['city'], 'scene': status['scene'], 'player': player})
        text = log()
        assert 'breaking Disregard' not in text and 'Fatal error:' not in text
        (args.output / (game + '.log')).write_text(text)
        results.append({'game': game, 'collections': 5, 'slots': 100, 'visited': visited})
        print(json.dumps(results[-1], ensure_ascii=False), flush=True)
except BaseException:
    (args.output / 'Failure.log').write_text(log())
    (args.output / 'Crash.log').write_bytes(adb('logcat', '-b', 'crash', '-d', check=False).stdout)
    (args.output / 'ExitInfo.txt').write_bytes(adb('shell', 'dumpsys', 'activity', 'exit-info', args.package, check=False).stdout)
    raise
finally:
    adb('shell', 'am', 'force-stop', args.package)
    for name in save_names():
        if name not in backups:
            adb('shell', 'rm', '-f', saved + '/' + name)
    for name, contents in backups.items():
        write(saved + '/' + name, contents)
        assert read(saved + '/' + name) == contents, 'Save restore mismatch: ' + name
    if old_choice is None:
        adb('shell', 'rm', '-f', preference, check=False)
    else:
        write(preference, old_choice)
    (args.output / 'Results.json').write_text(json.dumps({'results': results, 'preservedSaves': {name: hashlib.sha256(value).hexdigest() for name, value in backups.items()}}, ensure_ascii=False, indent=2))
    launch()
