Download `ChronoStroke.exe` below and run it. There is nothing to install — it is a single
self-contained file and needs no .NET runtime on the machine.

Windows 11, x64.

### SmartScreen will warn you the first time

The executable is not code-signed, so Windows shows *"Windows protected your PC"*. Choose
**More info → Run anyway**, or verify the download against the checksum first:

```powershell
(Get-FileHash .\ChronoStroke.exe -Algorithm SHA256).Hash.ToLower()
```

Expected:

```
__SHA256__
```

---

Built from `__COMMIT__` by [this workflow run](__RUN_URL__).
