"""Validate experimental publish output and write ZIPs with Unix executable modes."""
import argparse
import hashlib
import json
import plistlib
import stat
import struct
import zipfile
from pathlib import Path


def architectures(path):
    with path.open('rb') as stream:
        header = stream.read(8)
        if len(header) < 8:
            return set()
        if header[:4] == b'\xcf\xfa\xed\xfe':
            return {struct.unpack('<I', header[4:])[0]}
        if header[:4] == b'\xca\xfe\xba\xbe':
            count = struct.unpack('>I', header[4:])[0]
            return {struct.unpack('>I', stream.read(20)[:4])[0] for _ in range(count)}
    return set()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def archive_tree(root, destination):
    records = []
    with zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(root.rglob('*')):
            relative = path.relative_to(root).as_posix()
            is_directory = path.is_dir()
            executable = not is_directory and (bool(architectures(path)) or path.suffix == '.command')
            mode = (stat.S_IFDIR | 0o755) if is_directory else (stat.S_IFREG | (0o755 if executable else 0o644))
            entry = zipfile.ZipInfo(relative + ('/' if is_directory else ''), (2000, 1, 1, 0, 0, 0))
            entry.create_system = 3  # Unix, even when this script runs on Windows.
            entry.external_attr = mode << 16
            entry.compress_type = zipfile.ZIP_DEFLATED
            data = b'' if is_directory else path.read_bytes()
            archive.writestr(entry, data)
            if not is_directory:
                records.append({'path': relative, 'length': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
    with zipfile.ZipFile(destination) as archive:
        require(archive.testzip() is None, f'Corrupt ZIP: {destination}')
        for record in records:
            data = archive.read(record['path'])
            require(hashlib.sha256(data).hexdigest() == record['sha256'], f"ZIP mismatch: {record['path']}")
    return {'file': destination.name, 'length': destination.stat().st_size,
            'sha256': hashlib.sha256(destination.read_bytes()).hexdigest(), 'files': records}


def main():
    parser = argparse.ArgumentParser()
    for name in ('host-root', 'plugin-root', 'output', 'rid', 'version'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    host_root, plugin_root, output = Path(args.host_root), Path(args.plugin_root), Path(args.output)
    app = next(host_root.glob('*/MyAvaloniaManagement.app'))
    macos = app / 'Contents/MacOS'
    expected_cpu = {'osx-arm64': 0x0100000C, 'osx-x64': 0x01000007}[args.rid]
    for name in ('MyAvaloniaManagement', 'libcoreclr.dylib', 'libhostfxr.dylib',
                 'libAvaloniaNative.dylib', 'libSkiaSharp.dylib', 'libHarfBuzzSharp.dylib'):
        require((macos / name).is_file(), f'Missing native dependency: {name}')
        require(expected_cpu in architectures(macos / name), f'Wrong Mach-O architecture: {name}')
    for path in macos.rglob('*'):
        if path.is_file():
            cpus = architectures(path)
            require(not cpus or expected_cpu in cpus, f'Wrong native architecture: {path}')
    config = json.loads((macos / 'MyAvaloniaManagement.runtimeconfig.json').read_text('utf-8-sig'))
    require(bool(config['runtimeOptions'].get('includedFrameworks')), 'Host must include .NET runtime')
    require((macos / 'HelpWeb/index.html').is_file(), 'Missing offline help')
    require(not (macos / 'Controls').exists(), 'Plugin payload must stay outside the app')
    with (app / 'Contents/Info.plist').open('rb') as stream:
        plist = plistlib.load(stream)
    require(plist['CFBundleExecutable'] == 'MyAvaloniaManagement', 'Incorrect bundle entry point')

    plugin = plugin_root / 'Controls/MyPlugTest'
    for name in ('MyPlugTest.dll', 'MyPlugTest.deps.json', 'plugin.manifest.json',
                 'MyAvaloniaManagement.Icons.dll', 'EPPlus.dll', 'Flurl.dll', 'Flurl.Http.dll'):
        require((plugin / name).is_file(), f'Missing plugin file: {name}')
    manifest = json.loads((plugin / 'plugin.manifest.json').read_text('utf-8-sig'))
    require(set(manifest) == {'schemaVersion', 'pluginId', 'pluginVersion', 'entryPoint', 'sdk'}, 'Incorrect plugin manifest fields')
    require(manifest['schemaVersion'] == 2 and manifest['pluginId'] == 'myavalonia.plugin.my-plug-test', 'Incorrect plugin identity')
    deps = json.loads((plugin / 'MyPlugTest.deps.json').read_text('utf-8-sig'))
    require(deps['runtimeTarget']['name'].endswith('/' + args.rid), 'Plugin deps RID does not match host')
    for path in plugin.rglob('*.dll'):
        require(not path.name.startswith(('Avalonia.', 'Dock.', 'Microsoft.Extensions.', 'MyAvaloniaManagement.PluginSdk', 'CommunityToolkit.')), f'Shared assembly in plugin: {path}')
    # Every private runtime asset in the resolved deps graph must actually be shipped.
    private = {'EPPlus', 'EPPlus.Interfaces', 'Microsoft.IO.RecyclableMemoryStream',
               'System.Security.Cryptography.Pkcs', 'System.Security.Cryptography.Xml', 'Flurl', 'Flurl.Http', 'MyAvaloniaManagement.Icons'}
    for library, assets in deps['targets'][deps['runtimeTarget']['name']].items():
        if library.split('/')[0] in private:
            for kind in ('runtime', 'native'):
                for asset in assets.get(kind, {}):
                    require((plugin / Path(asset).name).is_file(), f'Missing resolved private dependency: {asset}')

    host_name = f'MyAvaloniaManagement-{args.version}-{args.rid}-experiment.zip'
    plugin_name = f'MyPlugTest-{manifest["pluginVersion"]}-{args.rid}-experiment.zip'
    evidence = {'runtimeIdentifier': args.rid, 'macOSRuntimeTested': False,
                'host': archive_tree(host_root, output / host_name),
                'plugin': archive_tree(plugin_root, output / plugin_name)}
    (output / f'{args.rid}-package-evidence.json').write_text(json.dumps(evidence, indent=2), encoding='utf-8')
    print(json.dumps({key: {k: v for k, v in evidence[key].items() if k != 'files'} for key in ('host', 'plugin')}, indent=2))


if __name__ == '__main__':
    main()
