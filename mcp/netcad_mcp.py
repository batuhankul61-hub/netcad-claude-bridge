# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=2.3,<3"]
# ///
"""Kul AI NETCAD MCP köprüsü.

Claude Code (ya da başka bir MCP istemcisi) ile NETCAD içindeki Kul AI modülü arasında köprü kurar.
Modülün araçlarını (çizim, nesne arama, ifraz, koordinat dönüşümü, ekran görüntüsü...) MCP aracı olarak sunar.

Kanal: modül, yanında mcp-inbox.txt varsa o dosyada yazan klasörü izler (scripts\\deploy.ps1 -Mcp).
Köprü oraya <ad>.cmd bırakır; modül komutu NETCAD'in ana iş parçacığında çalıştırıp <ad>.out yazar.
"""
import asyncio
import base64
import json
import os
import time
import uuid
from pathlib import Path

import mcp.types as types
from mcp.server.lowlevel import Server
from mcp.server.stdio import stdio_server

INBOX = Path(os.environ.get("NETCAD_MCP_INBOX") or Path(os.environ["APPDATA"]) / "KulAI.Netcad" / "mcp-inbox")
TOOL_TIMEOUT = float(os.environ.get("NETCAD_MCP_TIMEOUT", "180"))
# Son okunan araç tanımları: NETCAD kapalıyken de araç listesi sunulabilsin.
SCHEMA_CACHE = Path(__file__).with_name("schemas.json")

NOT_RUNNING = ("NETCAD cevap vermiyor. NETCAD 8.5 açık mı ve Kul AI modülü MCP kanalıyla mı kuruldu? "
               "(scripts\\deploy.ps1 -Mcp). Modal bir pencere açıksa önce onu kapatın.")

EXTRA_TOOLS = [
    types.Tool(name="netcad_status",
               description="NETCAD bağlantısını ve API erişimini kontrol eder (açık proje, nesne sayısı, katmanlar).",
               input_schema={"type": "object", "properties": {}}),
    types.Tool(name="netcad_new_project",
               description="NETCAD'de yeni, boş, kaydedilmemiş bir proje açar. Kullanıcının açık projelerine dokunmaz.",
               input_schema={"type": "object", "properties": {}}),
]

def _send(command: str, timeout: float) -> str | None:
    """Komutu kanala bırakır, cevabı bekler. Zaman aşımında None döner."""
    INBOX.mkdir(parents=True, exist_ok=True)
    name = f"mcp-{time.strftime('%H%M%S')}-{uuid.uuid4().hex[:8]}"
    cmd, out = INBOX / f"{name}.cmd", INBOX / f"{name}.out"
    cmd.write_text(command, encoding="utf-8")
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if out.exists():
            time.sleep(0.1)  # yazım bitsin
            text = out.read_text(encoding="utf-8-sig")
            out.unlink(missing_ok=True)
            return text
        time.sleep(0.2)
    # NETCAD almadıysa komut sonradan beklenmedik anda çalışmasın.
    cmd.unlink(missing_ok=True)
    return None


async def send(command: str, timeout: float = TOOL_TIMEOUT) -> str | None:
    return await asyncio.to_thread(_send, command, timeout)


def _to_tool(spec: dict) -> types.Tool:
    schema = spec.get("input_schema") or {"type": "object", "properties": {}}
    return types.Tool(name=spec["name"], description=spec.get("description") or "", input_schema=schema)


async def list_tools(ctx, params) -> types.ListToolsResult:
    specs = None
    text = await send("schemas", timeout=8)
    if text:
        try:
            specs = json.loads(text)
            SCHEMA_CACHE.write_text(json.dumps(specs, ensure_ascii=False, indent=1), encoding="utf-8")
        except ValueError:
            specs = None
    if specs is None and SCHEMA_CACHE.exists():
        specs = json.loads(SCHEMA_CACHE.read_text(encoding="utf-8"))
    return types.ListToolsResult(tools=EXTRA_TOOLS + [_to_tool(s) for s in specs or []])


def _parse(text: str) -> tuple[list[types.TextContent | types.ImageContent], bool]:
    """Modül cevabı: 'SONUÇ: ...' / 'HATA: ...', isteğe bağlı son satır 'GÖRÜNTÜ: a.jpg, b.jpg'."""
    is_error = text.startswith("HATA: ") or text.startswith("REDDEDİLDİ") or text.startswith("İSTİSNA")
    images: list[types.ImageContent] = []
    lines = text.splitlines()
    if lines and lines[-1].startswith("GÖRÜNTÜ: "):
        for path in lines.pop()[len("GÖRÜNTÜ: "):].split(", "):
            p = Path(path.strip())
            if p.exists():
                mime = "image/png" if p.suffix.lower() == ".png" else "image/jpeg"
                images.append(types.ImageContent(type="image", data=base64.b64encode(p.read_bytes()).decode(), mime_type=mime))
                p.unlink(missing_ok=True)
    body = "\n".join(lines)
    for prefix in ("SONUÇ: ", "HATA: "):
        if body.startswith(prefix):
            body = body[len(prefix):]
    return [types.TextContent(type="text", text=body), *images], is_error


async def call_tool(ctx, params: types.CallToolRequestParams) -> types.CallToolResult:
    name, arguments = params.name, params.arguments
    if name == "netcad_status":
        command = "diag"
    elif name == "netcad_new_project":
        command = "newproject"
    else:
        command = f"tool {name} {json.dumps(arguments or {}, ensure_ascii=False)}"
    text = await send(command)
    if text is None:
        return types.CallToolResult(content=[types.TextContent(type="text", text=NOT_RUNNING)], is_error=True)
    content, is_error = _parse(text)
    return types.CallToolResult(content=content, is_error=is_error)


server = Server("netcad", instructions=(
    "NETCAD 8.5 içindeki Kul AI modülünün araçları. Koordinatlar [east, north] = [harita Y, harita X] sırasıyla verilir. "
    "Çizimi değiştiren her araç tek bir geri alma adımıdır (NETCAD'de Ctrl+Z)."),
    on_list_tools=list_tools, on_call_tool=call_tool)


async def main() -> None:
    async with stdio_server() as (read, write):
        await server.run(read, write, server.create_initialization_options())


if __name__ == "__main__":
    asyncio.run(main())
