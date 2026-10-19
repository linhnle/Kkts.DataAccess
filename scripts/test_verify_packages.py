"""Regression tests for the local NuGet release verifier."""

from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest
import xml.etree.ElementTree as ET
from zipfile import ZipFile

from verify_packages import CORE, EF, PACKAGE_DEPENDENCIES, uses_version, verify_packages


VERSION = "0.1.1-localcheck"
NAMESPACE = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"


class VerifyPackagesTests(unittest.TestCase):
    def setUp(self):
        self.temporary = TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        for name in PACKAGE_DEPENDENCIES:
            self.write_package(name)

    def write_package(
        self, name, *, package_id=None, version=VERSION, dependency_version=VERSION,
        include_readme=True, include_assembly=True, include_dependencies=True,
    ):
        root = ET.Element("package", xmlns=NAMESPACE)
        metadata = ET.SubElement(root, "metadata")
        ET.SubElement(metadata, "id").text = package_id or name
        ET.SubElement(metadata, "version").text = version
        ET.SubElement(metadata, "readme").text = "README.md"
        dependencies = ET.SubElement(metadata, "dependencies")
        group = ET.SubElement(dependencies, "group", targetFramework="net10.0")
        if include_dependencies:
            for dependency in PACKAGE_DEPENDENCIES[name]:
                ET.SubElement(group, "dependency", id=dependency, version=dependency_version)
        with ZipFile(self.directory / f"{name}.{VERSION}.nupkg", "w") as package:
            package.writestr(f"{name}.nuspec", ET.tostring(root))
            if include_assembly:
                package.writestr(f"lib/net10.0/{name}.dll", b"fixture assembly")
            if include_readme:
                package.writestr("README.md", "Fixture README")

    def test_complete_release(self):
        verify_packages(self.directory, VERSION)

    def test_dependency_version_forms(self):
        for declaration in (VERSION, f"[{VERSION}]", f"[{VERSION}, )", f"[{VERSION}, 1.0.0)"):
            with self.subTest(declaration=declaration):
                self.assertTrue(uses_version(declaration, VERSION))
                self.write_package(EF, dependency_version=declaration)
                verify_packages(self.directory, VERSION)
        for declaration in ("", "0.1.0", f"({VERSION}, )", "(,1.0.0]", f"{VERSION}0"):
            with self.subTest(declaration=declaration):
                self.assertFalse(uses_version(declaration, VERSION))

    def test_missing_directory(self):
        with self.assertRaisesRegex(ValueError, "does not exist"):
            verify_packages(self.directory / "absent", VERSION)

    def test_missing_package(self):
        (self.directory / f"{CORE}.{VERSION}.nupkg").unlink()
        with self.assertRaisesRegex(ValueError, "missing=.*Kkts.DataAccess"):
            verify_packages(self.directory, VERSION)

    def test_unexpected_package(self):
        (self.directory / "EmployeeApp.0.1.0.nupkg").write_bytes(b"unexpected")
        with self.assertRaisesRegex(ValueError, "unexpected=.*EmployeeApp"):
            verify_packages(self.directory, VERSION)

    def test_wrong_expected_version(self):
        with self.assertRaisesRegex(ValueError, "Package set mismatch"):
            verify_packages(self.directory, "0.1.0")

    def test_wrong_manifest_version(self):
        self.write_package(CORE, version="0.1.0")
        with self.assertRaisesRegex(ValueError, "manifest version"):
            verify_packages(self.directory, VERSION)

    def test_wrong_package_id(self):
        self.write_package(CORE, package_id="Unexpected")
        with self.assertRaisesRegex(ValueError, "manifest package ID"):
            verify_packages(self.directory, VERSION)

    def test_wrong_internal_dependency(self):
        self.write_package(EF, dependency_version="0.1.0")
        with self.assertRaisesRegex(ValueError, "dependency Kkts.DataAccess uses"):
            verify_packages(self.directory, VERSION)

    def test_missing_internal_dependency(self):
        self.write_package(EF, include_dependencies=False)
        with self.assertRaisesRegex(ValueError, "internal dependencies"):
            verify_packages(self.directory, VERSION)

    def test_missing_readme(self):
        self.write_package(CORE, include_readme=False)
        with self.assertRaisesRegex(ValueError, "README"):
            verify_packages(self.directory, VERSION)

    def test_missing_assembly(self):
        self.write_package(CORE, include_assembly=False)
        with self.assertRaisesRegex(ValueError, "lib/net10.0"):
            verify_packages(self.directory, VERSION)

    def test_cli_failure(self):
        result = subprocess.run(
            [sys.executable, str(Path(__file__).with_name("verify_packages.py")),
             str(self.directory), "0.1.0"],
            capture_output=True, text=True, check=False,
        )
        self.assertEqual(result.returncode, 1)
        self.assertIn("Package verification failed:", result.stderr)
        self.assertNotIn("Verified all", result.stdout)


if __name__ == "__main__":
    unittest.main()
