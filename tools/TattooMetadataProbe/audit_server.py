"""Read local server resources and compare them to the GTA tattoo audit.

This reads static configuration; it never executes Lua or edits source resources.
"""

import collections
import csv
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET


def joaat(value):
    result = 0
    for byte in value.lower().encode("utf-8"):
        result = (result + byte) & 0xFFFFFFFF
        result = (result + (result << 10)) & 0xFFFFFFFF
        result ^= result >> 6
    result = (result + (result << 3)) & 0xFFFFFFFF
    result ^= result >> 11
    return (result + (result << 15)) & 0xFFFFFFFF


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def main():
    if len(sys.argv) < 4:
        raise SystemExit("Usage: audit_server.py <GTA audit.json> <output folder> <resource root> [...]")
    audit_path = Path(sys.argv[1]).resolve()
    output = Path(sys.argv[2]).resolve()
    roots = [Path(value).resolve() for value in sys.argv[3:]]
    audit = json.loads(audit_path.read_text(encoding="utf-8"))
    targets = [entry for entry in audit["Results"] if not entry["Matches"] and not entry["Candidates"]]
    name_owners = collections.defaultdict(set)
    hash_owners = collections.defaultdict(set)
    for entry in targets:
        for name in {entry["Dictionary"].lower(), entry["Texture"].lower()}:
            name_owners[name].add(entry["Id"])
            for candidate in (name, name + "_m", name + "_f"):
                hashed = joaat(candidate)
                for value in (str(hashed), str(hashed if hashed < 2**31 else hashed - 2**32), f"0x{hashed:08x}", f"hash_{hashed:08x}"):
                    hash_owners[value].add(entry["Id"])
    names = re.compile(r"(?<![a-z0-9_])(?:" + "|".join(re.escape(name) for name in sorted(name_owners, key=len, reverse=True)) + r")(?:_[mf])?(?![a-z0-9_])", re.I)
    hashes = re.compile(r"(?<![a-z0-9_])(?:hash_[0-9a-f]{8}|0x[0-9a-f]{8}|-?[0-9]{6,10})(?![a-z0-9_])", re.I)
    command = ["rg", "--files", "-uuu", *map(str, roots)]
    for extension in ("lua", "xml", "meta", "json", "js", "ts", "cfg", "ini", "toml", "ymt", "rpf"):
        command += ["-g", "*." + extension]
    for excluded in ("node_modules", ".git", "dist"):
        command += ["-g", "!**/" + excluded + "/**"]
    files = [Path(value) for value in subprocess.check_output(command, text=True, encoding="utf-8").splitlines()]
    references = collections.defaultdict(list)
    hash_references = collections.defaultdict(list)
    metadata = collections.defaultdict(list)
    xml_errors = []
    binary_files = []
    overlay_documents = []
    text_files = 0
    xml_files = 0
    catalog_entries = []
    for path in files:
        if path.suffix.lower() in {".ymt", ".rpf"}:
            binary_files.append(str(path))
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        text_files += 1
        for number, line in enumerate(text.splitlines(), 1):
            for match in names.finditer(line):
                key = match.group().lower()
                owners = name_owners.get(key, set()) | name_owners.get(re.sub(r"_[mf]$", "", key), set())
                for owner in owners:
                    references[owner].append({"File": str(path), "Line": number, "Name": match.group()})
            for match in hashes.finditer(line):
                for owner in hash_owners.get(match.group().lower(), ()):
                    hash_references[owner].append({"File": str(path), "Line": number, "Hash": match.group()})
        if path.name in {"tattoos.lua", "client_config.lua"} and "tattoos" in path.parts:
            gender = None
            zone = None
            for number, line in enumerate(text.splitlines(), 1):
                model = re.search(r'GetHashKey\([\'"](mp_[mf]_freemode_01)[\'"]\)', line)
                if model:
                    gender = model.group(1)
                zone_match = re.match(r"\s*\[([0-5])\]\s*=\s*\{", line)
                if zone_match:
                    zone = int(zone_match.group(1))
                item = re.search(r"collection\s*=\s*['\"]([^'\"]+)['\"].*overlay\s*=\s*['\"]([^'\"]+)['\"]", line)
                if item and gender and zone is not None:
                    catalog_entries.append({"File": str(path), "Line": number, "Model": gender, "ZoneIndex": zone,
                        "Collection": item.group(1), "Overlay": item.group(2)})
        if path.suffix.lower() not in {".xml", ".meta"}:
            continue
        xml_files += 1
        try:
            # Some resources have an invalid encoding spelling in the declaration.
            # The text is already decoded; remove only that declaration in memory.
            root = ET.fromstring(re.sub(r"^\s*<\?xml[^?]*\?>", "", text, count=1))
        except ET.ParseError as error:
            xml_errors.append({"File": str(path), "Error": str(error)})
            continue
        if local_name(root.tag) == "PedDecorationCollection":
            overlay_documents.append(str(path))
        for node in root.iter():
            fields = {local_name(child.tag): child for child in node}
            if "txdHash" not in fields or "txtHash" not in fields:
                continue
            dictionary = (fields["txdHash"].text or "").lower()
            texture = (fields["txtHash"].text or "").lower()
            for entry in targets:
                if dictionary != entry["Dictionary"].lower() or texture != entry["Texture"].lower():
                    continue
                metadata[entry["Id"]].append({"File": str(path), "Zone": fields.get("zone").text if "zone" in fields else None,
                    "Overlay": fields.get("nameHash").text if "nameHash" in fields else None})
    results = [{**entry, "ServerReferences": references[entry["Id"]], "NumericReferences": hash_references[entry["Id"]],
        "ServerMetadata": metadata[entry["Id"]]} for entry in targets]
    report = {"SourceAudit": str(audit_path), "Roots": list(map(str, roots)), "TextFilesScanned": text_files,
        "XmlMetaFilesScanned": xml_files, "XmlParseErrors": xml_errors, "BinaryFiles": binary_files,
        "OverlayDocuments": overlay_documents, "CatalogEntries": catalog_entries, "Targets": len(targets),
        "LiteralMatches": sum(bool(entry["ServerReferences"]) for entry in results),
        "NumericMatches": sum(bool(entry["NumericReferences"]) for entry in results),
        "MetadataMatches": sum(bool(entry["ServerMetadata"]) for entry in results), "Results": results}
    output.mkdir(parents=True, exist_ok=True)
    (output / "server-tattoo-audit.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    with (output / "server-tattoo-audit.csv").open("w", newline="", encoding="utf-8-sig") as destination:
        writer = csv.writer(destination, delimiter=";")
        writer.writerow(["Dictionary", "Texture", "LiteralReferences", "NumericReferences", "MetadataMatches"])
        for entry in results:
            writer.writerow([entry["Dictionary"], entry["Texture"], len(entry["ServerReferences"]), len(entry["NumericReferences"]), len(entry["ServerMetadata"])])
    for name in ("TextFilesScanned", "XmlMetaFilesScanned", "Targets", "LiteralMatches", "NumericMatches", "MetadataMatches"):
        print(f"{name}: {report[name]}")
    print(f"XML parse errors: {len(xml_errors)}; binary files pending inspection: {len(binary_files)}")
    print("Target dictionary groups:", dict(collections.Counter(re.sub(r"_\d+$", "", entry["Dictionary"]) for entry in targets)))


if __name__ == "__main__":
    main()
