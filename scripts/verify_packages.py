"""Verify a complete local NuGet release using only the Python standard library."""

import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
from zipfile import BadZipFile, ZipFile


CORE = "Kkts.DataAccess"
EF = f"{CORE}.EntityFrameworkCore"
PACKAGE_DEPENDENCIES = {
    CORE: set(),
    EF: {CORE},
    f"{CORE}.Sqlite": {CORE, EF},
    f"{CORE}.SqlServer": {EF},
    f"{CORE}.MySql": {EF},
    f"{CORE}.DependencyInjection": {CORE},
    f"{CORE}.AspNetCore": {CORE},
    f"{CORE}.DataProtection": {CORE},
    f"{CORE}.Testing": {CORE},
}


def uses_version(declaration: str, expected: str) -> bool:
    return (
        declaration == expected
        or declaration == f"[{expected}]"
        or (
            declaration.startswith(f"[{expected},")
            and declaration.endswith((")", "]"))
        )
    )


def verify_packages(directory: Path, version: str) -> None:
    if not directory.is_dir():
        raise ValueError(f"Package directory does not exist: {directory}")

    expected_files = {f"{name}.{version}.nupkg" for name in PACKAGE_DEPENDENCIES}
    actual_files = {path.name for path in directory.glob("*.nupkg")}
    if actual_files != expected_files:
        raise ValueError(
            f"Package set mismatch in {directory}: "
            f"missing={sorted(expected_files - actual_files)}, "
            f"unexpected={sorted(actual_files - expected_files)}"
        )

    for name, expected_dependencies in PACKAGE_DEPENDENCIES.items():
        path = directory / f"{name}.{version}.nupkg"
        with ZipFile(path) as package:
            manifests = [entry for entry in package.namelist() if entry.endswith(".nuspec")]
            if len(manifests) != 1:
                raise ValueError(f"{path.name}: expected one nuspec, found {len(manifests)}")
            metadata = ET.fromstring(package.read(manifests[0])).find("{*}metadata")
            if metadata is None:
                raise ValueError(f"{path.name}: missing nuspec metadata")
            if metadata.findtext("{*}id") != name:
                raise ValueError(f"{path.name}: manifest package ID must be {name}")
            if metadata.findtext("{*}version") != version:
                raise ValueError(f"{path.name}: manifest version must be {version}")

            assembly = f"lib/net10.0/{name}.dll"
            if assembly not in package.namelist() or not package.read(assembly):
                raise ValueError(f"{path.name}: missing or empty {assembly}")
            readme = metadata.findtext("{*}readme")
            if not readme or readme not in package.namelist() or not package.read(readme):
                raise ValueError(f"{path.name}: missing or empty package README")

            dependencies = set()
            for dependency in metadata.findall(".//{*}dependency"):
                dependency_id = dependency.get("id", "")
                if dependency_id not in PACKAGE_DEPENDENCIES:
                    continue
                declaration = dependency.get("version", "")
                if not uses_version(declaration, version):
                    raise ValueError(
                        f"{path.name}: dependency {dependency_id} uses "
                        f"{declaration!r}, expected version/lower bound {version}"
                    )
                dependencies.add(dependency_id)
            if dependencies != expected_dependencies:
                raise ValueError(
                    f"{path.name}: internal dependencies {sorted(dependencies)} "
                    f"do not match {sorted(expected_dependencies)}"
                )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Initially clean package output directory")
    parser.add_argument("version", help="Expected normalized NuGet release version")
    args = parser.parse_args()
    try:
        verify_packages(args.directory, args.version)
    except (ValueError, OSError, BadZipFile, ET.ParseError) as error:
        print(f"Package verification failed: {error}", file=sys.stderr)
        return 1
    print(f"Verified all {len(PACKAGE_DEPENDENCIES)} packages at version {args.version}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
