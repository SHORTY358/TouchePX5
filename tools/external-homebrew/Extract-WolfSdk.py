#!/usr/bin/env python3
# Copyright (C) 2026 SharpEmu Emulator Project
# SPDX-License-Identifier: GPL-2.0-or-later
"""Extract only open payload SDK build inputs from the pinned pacbrew bundle."""
import hashlib
import json
import shutil
import tarfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[2]
ARCHIVE = ROOT / "artifacts/external-homebrew/ps5-payload-dev-v0.40.2.tar.gz"
DESTINATION = ROOT / "artifacts/external-homebrew/pacbrew-v0.40.2"
EXPECTED = "a85f65de418a8e6a898c6c3e3c870d50fff7618a200e4dd59ea9692af6ecec4d"
PREFIX = "opt/ps5-payload-sdk/target/"
SELECTED = tuple(PREFIX + suffix for suffix in (
    "include/", "lib/", "user/homebrew/include/", "user/homebrew/lib/"))
SELECTED += ("opt/ps5-payload-sdk/ldscripts/",)


def safe_path(relative):
    name = PurePosixPath(relative)
    if name.is_absolute() or ".." in name.parts or "\\" in relative or ":" in relative:
        raise ValueError(f"Unsafe archive path: {relative}")
    path = (DESTINATION / relative).resolve()
    if not path.is_relative_to(DESTINATION.resolve()):
        raise ValueError(f"Escaping output path: {relative}")
    return path


def main():
    with ARCHIVE.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if digest != EXPECTED:
        raise ValueError("Unexpected dependency bundle SHA256")
    aliases = {}
    files = 0
    with tarfile.open(ARCHIVE, "r|gz") as archive:
        for member in archive:
            name = member.name.removeprefix("./")
            if not name.startswith(SELECTED):
                continue
            output = safe_path(name)
            if member.isfile():
                output.parent.mkdir(parents=True, exist_ok=True)
                with archive.extractfile(member) as source, output.open("wb") as target:
                    shutil.copyfileobj(source, target)
                files += 1
            elif member.issym() or member.islnk():
                # Materialize header aliases as regular files on Windows.
                target = (PurePosixPath(name).parent / member.linkname
                          if member.issym() else PurePosixPath(member.linkname))
                # Resolve relative '..' aliases inside the checked extraction root.
                resolved = (DESTINATION / str(target)).resolve()
                if not resolved.is_relative_to(DESTINATION.resolve()):
                    raise ValueError(f"Escaping header alias: {name}")
                aliases[output] = resolved
    def materialize(path, seen):
        if path in seen:
            raise ValueError(f"Cyclic header alias: {path}")
        target = aliases.get(path)
        if target is None:
            if not path.is_file():
                raise ValueError(f"Missing header alias target: {path}")
            return
        materialize(target, seen | {path})
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(target, path)
    for path in aliases:
        materialize(path, set())
    report = {"archive_sha256": digest, "files": files, "aliases": len(aliases),
              "destination": str(DESTINATION), "version": "v0.40.2"}
    (DESTINATION / "extraction-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
