"""Taps the centre of the first on-screen view whose text contains argv[1]."""
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

want = sys.argv[1]
root = ET.parse("ui.xml").getroot()
texts = []
for node in root.iter("node"):
    text = " ".join(node.get(k, "") for k in ("text", "content-desc", "hint"))
    texts.append(text.strip())
    if want in text:
        x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.get("bounds")))
        subprocess.run(["adb", "shell", "input", "tap", str((x1 + x2) // 2), str((y1 + y2) // 2)], check=True)
        sys.exit(0)
print(f"no view with text {want!r}; on screen: {[t for t in texts if t]}")
sys.exit(1)
