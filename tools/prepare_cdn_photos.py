"""Copy exported photographs into Touz CDN paths without changing image bytes."""

import argparse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import csv
import hashlib
from io import BytesIO
import json
from pathlib import Path
import re
import time
from uuid import UUID
from zipfile import ZipFile, ZIP_STORED

from PIL import Image


def uuid(value):
    return str(UUID(value))


def load_identities(source, identities):
    paths = [identities] if isinstance(identities, Path) else list(identities)
    native = source / "gta-clothing-identities.json"
    if native.is_file() and native.resolve() not in {path.resolve() for path in paths}:
        paths.append(native)
    if not paths:
        raise ValueError("Provide --identities or export the GTA identity map into the source folder.")
    entries = {}
    source_layout = False
    for path in paths:
        document = json.loads(path.read_text(encoding="utf-8-sig"))
        if document.get("formatVersion") != 1:
            raise ValueError("Unsupported identity map: " + str(path))
        source_layout |= document.get("preserveSourceLayout", False)
        for entry in document["entries"]:
            previous = entries.get(entry["id"])
            if previous is None:
                entries[entry["id"]] = entry
                continue
            if any(previous[key] != entry[key] for key in ("model", "kind", "slot", "collection", "index")):
                raise ValueError("Conflicting native address for UUID: " + entry["id"])
            textures = {texture["id"]: texture for texture in previous["textures"]}
            by_index = {}
            for texture in previous["textures"]:
                if texture.get("available", True):
                    by_index.setdefault(texture["index"], texture.get("aliasOf") or texture["id"])
            for texture in entry["textures"]:
                if texture["id"] in textures:
                    if textures[texture["id"]]["index"] != texture["index"]:
                        raise ValueError("Conflicting native texture index for UUID: " + texture["id"])
                    continue
                texture = dict(texture)
                if texture["index"] in by_index:
                    texture["aliasOf"] = by_index[texture["index"]]
                else:
                    by_index[texture["index"]] = texture["id"]
                textures[texture["id"]] = texture
            previous["textures"] = list(textures.values())
    return {"formatVersion": 1, "preserveSourceLayout": source_layout, "entries": list(entries.values())}


def prepare(source, destination, identities):
    if destination.exists():
        raise ValueError("Choose a new output directory; existing packages are never overwritten.")
    document = load_identities(source, identities)
    settings_file = source / "export-settings.json"
    all_textures = json.loads(settings_file.read_text(encoding="utf-8-sig")).get("allTextures", False) if settings_file.is_file() else False
    records = {}
    export_errors = []
    for number, line in enumerate((source / "index.jsonl").open(encoding="utf-8-sig"), 1):
        if not line.strip():
            continue
        entry = json.loads(line)
        if entry.get("status") == "error":
            export_errors.append({key: entry[key] for key in ("kind", "Name", "item", "Gender", "gender", "Category", "variant", "texture", "message") if key in entry})
        if entry.get("status") == "ok" and entry.get("file"):
            records[entry["file"].replace("\\", "/").casefold()] = entry

    def export_key(entry):
        return (entry["kind"], entry.get("item", entry.get("Name")), entry.get("Gender", entry.get("gender")),
            entry.get("variant", entry.get("texture", "")))

    recovered = {export_key(entry) for entry in records.values()}
    export_errors = [entry for entry in export_errors if export_key(entry) not in recovered]
    files = sorted(path for folder in ("Masculino", "Feminino", "tattoos", "aparencia")
        for path in (source / folder).rglob("*") if path.is_file() and path.suffix.lower() in (".webp", ".png", ".jpg", ".jpeg"))
    if not files:
        raise ValueError("No exported images found.")
    base = Path("game/images/nations3")
    slots = {"berd": "1", "uppr": "3", "lowr": "4", "hand": "5", "feet": "6", "teef": "7", "accs": "8",
        "task": "9", "decl": "10", "jbib": "11", "p_head": "p0", "p_eyes": "p1", "p_ears": "p2", "p_lwrist": "p6", "p_rwrist": "p7"}
    appearance = {"Barba": "facialHair", "Sobrancelhas": "eyebrows", "Blush": "blush", "Pelos corporais": "chestHair",
        "Lábios": "lipstick", "Olhos": "eyes", "Maquilhagem": "makeup", "Pintura facial": "makeup"}
    native_appearance = {"blemishes", "facialHair", "eyebrows", "ageing", "makeup", "blush", "complexion",
        "sunDamage", "lipstick", "freckles", "chestHair", "bodyBlemishes", "addBodyBlemishes"}
    plan = []
    targets = {}
    for path in files:
        relative = path.relative_to(source)
        record = records.get(relative.as_posix().casefold())
        if record is None:
            raise ValueError(f"Image absent from the export index: {relative}")
        kind = record["kind"]
        gender = record.get("Gender", record.get("gender", ""))
        english_gender = {"Masculino": "male", "Feminino": "female"}[gender]
        category = record.get("Category", record.get("appearanceKind", ""))
        target = None
        asset = ""
        texture = ""
        if kind == "drawable":
            asset = uuid(record["itemId"])
            texture = uuid(record["textureId"]) if record.get("textureId") else ""
            expected_stem = asset + "." + (texture or "sem_textura")
            if path.stem.lower() != expected_stem:
                raise ValueError(f"Filename does not match indexed UUIDs: {relative}")
            code = re.search(r"\[([^]]+)\]$", category)
            code = code.group(1) if code else ""
            if code == "hair":
                target = base / "nation_barbershop" / english_gender / "hair" / path.name.lower()
            elif code in slots:
                target = base / "nation_skinshop/clothes" / english_gender / slots[code] / path.name.lower()
        elif kind in ("tattoo", "appearance"):
            pieces = path.stem.split(".")
            asset = uuid(pieces[0])
            if kind == "appearance" and (len(pieces) != 2 or not pieces[1].isdigit()):
                raise ValueError(f"Unexpected appearance filename: {relative}")
            if kind == "tattoo":
                target = base / "nation_tattoos" / english_gender / path.name.lower()
            elif record.get("nativeCategory") in native_appearance:
                target = base / "nation_barbershop" / english_gender / record["nativeCategory"] / path.name.lower()
            elif category in appearance:
                target = base / "nation_barbershop" / english_gender / appearance[category] / path.name.lower()
        else:
            raise ValueError(f"Unexpected indexed kind: {kind}")
        status = "ready" if target else "unclassified"
        target = target or Path("pending") / kind / english_gender / path.name.lower()
        key = target.as_posix().casefold()
        if key in targets:
            raise ValueError(f"Destination collision: {relative} and {targets[key]}")
        targets[key] = str(relative)
        plan.append({"source": path, "relative": relative.as_posix(), "target": target,
            "kind": kind, "gender": gender, "category": category, "status": status,
            "asset": asset, "texture": texture, "extension": path.suffix.lower()})
    missing_indexed = [record["file"] for key, record in records.items()
        if not (source / Path(record["file"].replace("\\", "/"))).is_file()]
    if missing_indexed:
        raise ValueError(f"{len(missing_indexed)} indexed photographs are missing from disk.")
    destination.mkdir(parents=True)
    upload = destination / "upload"
    started = time.monotonic()

    def copy(entry):
        data = entry["source"].read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        with Image.open(BytesIO(data)) as image:
            width, height = image.size
            image.verify()
        target = (upload if entry["status"] == "ready" else destination) / entry["target"]
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(data)
        if hashlib.sha256(target.read_bytes()).hexdigest() != digest:
            raise ValueError(f"Copy verification failed: {entry['relative']}")
        return {key: value for key, value in entry.items() if key not in ("source", "target")} | {
            "target": entry["target"].as_posix(), "url": "https://cdn.touz.dev/" + entry["target"].as_posix() if entry["status"] == "ready" else "",
            "bytes": len(data), "sha256": digest, "width": width, "height": height}

    completed = []
    with ThreadPoolExecutor(max_workers=6) as executor:
        for entry in executor.map(copy, plan):
            completed.append(entry)
            if len(completed) % 3000 == 0:
                print(f"Verified and copied {len(completed)}/{len(plan)} images", flush=True)
    manifest = destination / "manifest.csv"
    with manifest.open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(completed[0]), delimiter=";")
        writer.writeheader(); writer.writerows(completed)
    mapped = {(entry["asset"], entry["texture"]) for entry in completed if entry["kind"] == "drawable" and entry["texture"]}
    missing = []
    (destination / "clothing-identities.json").write_text(json.dumps(document, indent=2), encoding="utf-8")
    for entry in document["entries"]:
        if not entry.get("available", True):
            continue
        available = [texture for texture in entry["textures"] if texture.get("available", True)]
        required = sorted(available, key=lambda texture: texture["index"])
        for texture in required if all_textures else required[:1]:
            if texture.get("available", True) and (entry["id"], texture["id"]) not in mapped:
                missing.append({"itemId": entry["id"], "textureId": texture["id"], "name": entry["name"],
                    "index": texture["index"], "inherited": texture.get("inherited", False)})
    template = "https://cdn.touz.dev/game/images/nations3/nation_skinshop/clothes/{gender}/{slot}/{itemId}.{textureId}.webp"
    ready = [entry for entry in completed if entry["status"] == "ready"]
    summary = {"images": len(completed), "bytes": sum(entry["bytes"] for entry in completed),
        "texture_mode": "all" if all_textures else "first",
        "ready_images": len(ready), "unclassified_images": len(completed) - len(ready),
        "by_kind": dict(Counter(entry["kind"] for entry in completed)),
        "image_dimensions": dict(Counter(f"{entry['width']}x{entry['height']}" for entry in completed)),
        "drawable_images_with_texture_uuid": len(mapped),
        "drawable_images_without_texture_uuid": sum(entry["kind"] == "drawable" and not entry["texture"] for entry in completed),
        "export_failures": len(export_errors), "export_failure_details": export_errors,
        "missing_mapped_images": len(missing), "missing_inherited_images": sum(entry["inherited"] for entry in missing),
        "missing_explicit_images": sum(not entry["inherited"] for entry in missing),
        "missing_default_images": sum(entry["index"] == 0 for entry in missing),
        "missing_explicit_default_images": [entry for entry in missing if entry["index"] == 0 and not entry["inherited"]],
        "image_url_template": template,
        "notes": ["Source images were read only; copies retain identical bytes and UUIDs.",
            "Existing nations3 folders are retained; UUID filenames and original image bytes are unchanged.",
            "Unclassified images remain in pending, outside the upload folder.",
            "The server image manifest must be regenerated to associate appearance and tattoo catalog entries.",
            "Coverage checks follow the first/all texture mode saved in export-settings.json.",
            "Missing inherited texture variants are reported; no placeholder images or guessed aliases were created."]}
    (destination / "report.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
    with (destination / "missing-images.csv").open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=["itemId", "textureId", "name", "index", "inherited"], delimiter=";")
        writer.writeheader(); writer.writerows(missing)
    instructions = "\n".join([
        "NATIONS3 CDN PHOTO PACKAGE", "",
        "Extract touz-upload.zip at the web-assets root, or copy the contents of upload/.",
        "The existing male/female/category folder hierarchy is preserved, along with UUID filenames and original image bytes.",
        "Hair uses nation_barbershop/<gender>/hair; clothing uses nation_skinshop/clothes/<gender>/<slot>.",
        "Tattoo images use nation_tattoos/<gender>; appearance uses the existing barbershop category names.",
        "Unclassified images are retained in pending/ and excluded from the upload ZIP.",
        "manifest.csv provides the exact URL and SHA-256; report.json and missing-images.csv report coverage and failures.",
        "clothing-identities.json combines the provided maps with gta-clothing-identities.json from the export folder.",
        "Regenerate vrp/config/AppearanceImages.json with the high repository tool and the combined map to connect the catalog to these files.", ""])
    (destination / "INSTRUCOES.txt").write_text(instructions, encoding="utf-8-sig")
    archive_path = destination / "touz-upload.zip"
    with ZipFile(archive_path, "x", compression=ZIP_STORED) as archive:
        for entry in completed:
            if entry["status"] == "ready":
                archive.write(upload / entry["target"], entry["target"])
    with ZipFile(archive_path) as archive:
        bad_file = archive.testzip()
        if bad_file:
            raise ValueError("Archive integrity failure: " + bad_file)
        if len(archive.infolist()) != len(ready):
            raise ValueError("Archive image count mismatch.")
    print(json.dumps({"output": str(destination), "images": summary["images"], "bytes": summary["bytes"],
        "export_failures": len(export_errors), "archive_bytes": archive_path.stat().st_size,
        "seconds": round(time.monotonic() - started, 1)}, ensure_ascii=True), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--identities", type=Path, nargs="+", default=[])
    args = parser.parse_args()
    prepare(args.source.resolve(), args.output.resolve(), [path.resolve() for path in args.identities])
