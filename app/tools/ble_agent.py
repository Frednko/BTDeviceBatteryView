"""BatteryBar BLE 广播助手（无 Python 依赖的打包版本用）

用法：
    BleAgent.exe            扫描 8 秒，输出 JSON 到 stdout
    BleAgent.exe --seconds 5

输出：一个 JSON 数组，每项:
    { "addr": "C4:2F:8E:54:AD:CC", "name": "AirPods Pro",
      "apple": { "model": "0E20", "left": 80, "right": 80, "case": 55,
                 "charging": {"left": false, "right": false, "case": true} },
      "fastpair": { "model": "FFFFFF", "battery": [80, 80, 55] } }
解析失败时对应字段为 null。
"""

import asyncio
import json
import sys

from bleak import BleakScanner

APPLE_COMPANY_ID = 0x004C
FASTPAIR_SERVICE = "0000fe2c-0000-1000-8000-00805f9b34fb"


def parse_apple(payload: bytes):
    """Apple Continuity 0x07 (Proximity Pairing) 的电量解析。

    头部固定为 07 19 01 <model:2> ，之后是状态与电量字节。
    电量 nibble：0-10 表示 0%-100%，0xF 表示未知。
    由于不同型号略有差异，这里给出保守解析并在字段缺失时返回 None。
    """
    if len(payload) < 12 or payload[0] != 0x07:
        return None

    model = "".join(f"{b:02X}" for b in payload[3:5])

    def pct(nibble):
        if nibble == 0xF:
            return None
        return max(0, min(100, nibble * 10))

    # 常见布局：byte8 低半字节=左、高半字节=右；byte9 为充电位；byte10 为充电盒
    left = pct(payload[8] & 0x0F)
    right = pct((payload[8] >> 4) & 0x0F)
    charging = payload[9] if len(payload) > 9 else 0
    case = pct(payload[10] & 0x0F) if len(payload) > 10 else None

    return {
        "model": model,
        "left": left,
        "right": right,
        "case": case,
        "charging": {
            "left": bool(charging & 0x02),
            "right": bool(charging & 0x01),
            "case": bool(charging & 0x04),
        },
    }


def parse_fastpair(payload: bytes):
    """Google Fast Pair 服务数据：前 3 字节 Model ID，随后是电池信息。"""
    if len(payload) < 5:
        return None
    model = "".join(f"{b:02X}" for b in payload[0:3])
    battery = []
    for b in payload[3:7]:
        if b == 0xFF:
            battery.append(None)
        else:
            battery.append(min(100, (b & 0x7F) * 2))
    return {"model": model, "battery": battery}


async def scan(seconds: float):
    results = {}

    def callback(device, adv):
        entry = results.setdefault(
            device.address,
            {"addr": device.address, "name": device.name or "", "apple": None, "fastpair": None},
        )
        if adv.local_name and not entry["name"]:
            entry["name"] = adv.local_name
        for cid, data in adv.manufacturer_data.items():
            if cid == APPLE_COMPANY_ID and data and data[0] == 0x07:
                entry["apple"] = parse_apple(bytes(data))
        for uuid, data in adv.service_data.items():
            if str(uuid).lower() == FASTPAIR_SERVICE:
                entry["fastpair"] = parse_fastpair(bytes(data))

    scanner = BleakScanner(detection_callback=callback, scanning_mode="active")
    await scanner.start()
    await asyncio.sleep(seconds)
    await scanner.stop()

    out = [v for v in results.values() if v["apple"] or v["fastpair"]]
    return out


def main():
    seconds = 8.0
    args = sys.argv[1:]
    if "--seconds" in args:
        try:
            seconds = float(args[args.index("--seconds") + 1])
        except Exception:
            pass
    try:
        data = asyncio.run(scan(seconds))
    except Exception as exc:  # 蓝牙关闭 / 无权限等
        print(json.dumps({"error": str(exc)}, ensure_ascii=False))
        return 1
    print(json.dumps(data, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
