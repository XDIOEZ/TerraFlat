import os
import json
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[3]
package_cache = root / "Library" / "PackageCache"
packages = sorted(package_cache.glob("com.gilzoide.sqlite-net@*")) if package_cache.exists() else []
packages = [package for package in packages if (package / "package.json").is_file()
            and json.loads((package / "package.json").read_text(encoding="utf-8"))["version"] == "1.3.3"]
driver = packages[0] if packages else root / "Library" / "FlatWorldGameplayTests" / "Dependencies" / "sqlite-net-1.3.3"
if not (driver / "Runtime" / "sqlite-net" / "SQLite.cs").is_file():
    driver.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["git", "clone", "--depth", "1", "--branch", "1.3.3",
                    "https://github.com/gilzoide/unity-sqlite-net.git", str(driver)], check=True)
project = Path(__file__).with_name("SqlitePersistence.Tests.csproj")
isolated = root / "Library" / "FlatWorldGameplayTests" / "SqlitePersistence"
isolated.mkdir(parents=True, exist_ok=True)
command = ["dotnet", "run", "--project", str(project), "--configuration", "Release",
           "-p:SQLiteDriverRoot=" + str(driver), "--", str(isolated)]
sys.exit(subprocess.call(command, env=os.environ.copy()))
