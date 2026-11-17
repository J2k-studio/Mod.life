# Husky Pal — Manual DLL References

โปรเจกต์นี้ปรับรูปแบบการอ้างอิง DLL ให้เหมือนโปรเจกต์เก่า `spinKer.csproj` ที่อยู่ใน `jaokupp-main.zip`:
ใช้ `TargetFramework` แบบ `net9.0` และอ้างอิง `StardewModdingAPI.dll`, `StardewValley.dll` และ `MonoGame.Framework.dll` ด้วย `HintPath` จากโฟลเดอร์ `lib/` โดยตรง

## เตรียม DLL

ใส่ DLL แบบ managed .NET ที่ตรงกับเวอร์ชัน Stardew Valley/SMAPI Android ของคุณใน `lib/`:

- `StardewModdingAPI.dll`
- `StardewValley.dll`
- `MonoGame.Framework.dll`

ถ้า compiler แจ้งว่าขาด assembly อื่น ให้ใส่ DLL ที่ตรงกันเพิ่มใน `lib/` และเพิ่ม Reference ใน `HuskyPal.csproj` ตามชื่อ assembly นั้น (มี optional references สำหรับ `xTile.dll` และ `SMAPI.Toolkit.CoreInterfaces.dll` ไว้แล้ว)

`Newtonsoft.Json 13.0.3` จะติดตั้งผ่าน NuGet ตอน restore อัตโนมัติ

## Build

```sh
dotnet restore
dotnet build -c Release -m:1 -p:UseSharedCompilation=false
```

Output:

```text
build/HuskyPal/
```

คัดลอกโฟลเดอร์ output ทั้งหมดไปไว้ใน `Mods/` ของเกม

## หมายเหตุ

- ไม่ใช้ `Pathoschild.Stardew.ModBuildConfig`
- ไม่ค้นหาโฟลเดอร์เกม PC และไม่ deploy อัตโนมัติ
- `.so` เป็น native library ไม่สามารถใช้แทน managed reference DLL ใน C# ได้
- การคอมไพล์ผ่านไม่ได้ยืนยันว่ามอดจะทำงานบน Android runtime; ต้องใช้ API และ assembly ที่เข้ากันกับ SMAPI Android รุ่นเป้าหมาย
