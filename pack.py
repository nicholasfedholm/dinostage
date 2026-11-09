import hashlib, os, sys, zipfile
root = os.path.dirname(os.path.abspath(__file__))
dll = os.path.join(root, "src", "DinoStage", "bin", "Release", "DinoStage.dll")
info = os.path.join(root, "src", "DinoStage", "modinfo.json")
out = os.path.join(root, "out")
os.makedirs(out, exist_ok=True)
zp = os.path.join(out, "dinostage_1.1.0.zip")
if os.path.exists(zp):
    os.remove(zp)
with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED) as z:
    z.write(info, "modinfo.json")
    z.write(dll, "DinoStage.dll")
with zipfile.ZipFile(zp) as z:
    print("testzip:", z.testzip(), "files:", [(i.filename, i.file_size) for i in z.infolist()])
data = open(zp, "rb").read()
print("zip", zp, "size", os.path.getsize(zp), "md5", hashlib.md5(data).hexdigest(), "sha256", hashlib.sha256(data).hexdigest())
