"""Install a built candidate into a temporary application. No registry uploads."""

import os
from pathlib import Path
import subprocess
import tempfile
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "artifacts"


def run(command, cwd, env=None):
    subprocess.run([str(value) for value in command], cwd=cwd, env=env, check=True)


with tempfile.TemporaryDirectory(prefix="originchaindb-install-") as temporary:
    app = Path(temporary)
    artifact = next(ARTIFACTS.glob("*.nupkg"))
    with zipfile.ZipFile(artifact) as package:
        spec = ET.fromstring(
            package.read(
                next((name for name in package.namelist() if name.endswith(".nuspec")))
            )
        )
    version = next((node.text for node in spec.iter() if node.tag.endswith("}version")))
    (app / "InstallCheck.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="OriginChainDB" Version="'
        + version
        + '" /></ItemGroup></Project>'
    )
    from xml.sax.saxutils import escape

    (app / "NuGet.Config").write_text(
        '<configuration><packageSources><clear/><add key="local" value="'
        + escape(str(ARTIFACTS), {'"': "&quot;"})
        + '"/></packageSources></configuration>'
    )
    (app / "Program.cs").write_text(
        'using OriginChainDB;using var db=new Client("https://example.invalid","test");var pages=db.Rows.PagesAsync("app.items","id",new PageOptions());var batch=new BatchResult(0,1,1);'
    )
    run(
        ["dotnet", "run", "--project", "InstallCheck.csproj", "-c", "Release"],
        app,
        dict(os.environ, NUGET_PACKAGES=str(app / "packages")),
    )
print("Fresh package application passed: " + "csharp")
