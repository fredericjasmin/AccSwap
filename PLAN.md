# GLAS — Game Launcher Account Switcher

Plan de desarrollo para un switcher de cuentas de gaming para Windows, inspirado en TcNo Acc-Switcher, AccShift y Epic Switcher (legacy), con objetivo de superarlos en robustez y seguridad.

- **Nombre:** GLAS (Game Launcher Account Switcher)
- **Stack:** C# + .NET 10 (LTS) + WPF (patrón MVVM)
- **Plataforma objetivo:** Windows 10/11
- **Alcance v0.1:** Steam + Epic Games Launcher

> Estado: **Fases 0–3 completadas y verificadas (build limpio, 37 tests verdes, publish de exe único y arranque comprobado).** Fase 4 parcial: flujo "añadir cuenta" e i18n ES/EN incluidos. Ver "Pendiente" al final.

---

## 1. Referencias de estudio

Fuentes en `otros/`:

| Carpeta | Stack | Qué tomar de cada uno |
|---|---|---|
| `TcNo-Acc-Switcher-master` | Go + Wails | Features avanzadas: atajos de escritorio por cuenta, tray, i18n, updater, módulo `vdfsafe` para manipular VDF sin romperlo |
| `accshift-main` | Tauri + Rust + Svelte | Diseño del core limpio (`crates/accshift-core`): snapshots, lock de procesos, cifrado (`snapshot_crypto.rs`), secrets |
| `epic-switcher-legacy-main` | C# + WPF + MVVM | Esqueleto MVVM y lógica Epic completa en `Services/EpicService.cs` (~150 líneas) |

Orden de lectura ya realizado:
1. `epic-switcher-legacy-main/Services/EpicService.cs` — lógica esencial de switching
2. `accshift-main/crates/accshift-core/src/platforms/` — diseño de la abstracción de plataforma
3. `TcNo-Acc-Switcher-master/internal/steam/` — detalles Windows de Steam (AutoLoginUser en registro, kill de steamwebhelper)

## 2. Lógica esencial del switching

El patrón que siguen todos los switchers:

```
detectar instalación -> listar cuentas guardadas -> snapshot de sesión actual
-> cerrar proceso del launcher -> restaurar snapshot elegido -> relanzar
```

Por plataforma (implementado en GLAS.Core):

- **Epic:** una cuenta = copia de `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\Windows\GameUserSettings.ini`. Switch = matar `EpicGamesLauncher.exe` → escribir el ini guardado → relanzar. Cuenta activa detectada por hash del token `Data=` (tolera que Epic reescriba otros ajustes del ini).
- **Steam:** cuenta = SteamID64. Switch = matar `steam`/`steamwebhelper`/`GameOverlayUI` → escribir `config/loginusers.vdf` con `MostRecent=1`/`RememberPassword=1` → fijar `HKCU\Software\Valve\Steam\AutoLoginUser` → relanzar `steam.exe -silent`.
- VDF parseado/escrito con un parser propio que preserva campos desconocidos y falla en vez de escribir un archivo corrupto.

## 3. Arquitectura (implementada)

```
GLAS.slnx
├── GLAS.Core/                      # motor, sin dependencias de UI (net10.0-windows)
│   ├── Account.cs, Platforms/      # IPlatform, PlatformRegistry
│   ├── Steam/ (Vdf/, LoginUsersFile, SteamPlatform, SteamPaths)
│   ├── Epic/ (EpicPlatform, EpicPaths)
│   ├── Snapshots/ (SnapshotStore, SwitchTransaction)
│   ├── Files/AtomicFile.cs         # escritura atómica (temp + File.Replace)
│   ├── Security/                   # AES-GCM + PBKDF2 (protección opcional)
│   ├── ExportImport/AccountPackage # paquete .glas (zip) con cifrado opcional
│   ├── RegistryAccess/, Processes/ # abstracciones inyectables para tests
│   └── Settings/AppSettings.cs
├── GLAS.App/                       # WPF (MVVM, CommunityToolkit)
│   ├── ViewModels/ (Main, AddAccount, Settings)
│   ├── Views: MainWindow, AddAccountWindow, SettingsWindow, PasswordWindow
│   ├── Services/ (AppServices, TrayService, HotkeyService, AutoStartService,
│   │             AppIcon, UiDialogs)
│   ├── Localization/Loc.cs         # ES/EN
│   └── Theme/Styles.xaml           # tema oscuro
└── GLAS.Core.Tests/                # 37 tests xUnit con carpetas simuladas
```

Reglas de diseño (cumplidas):
- `GLAS.Core` nunca toca la instalación real en los tests: rutas, registro y procesos inyectados.
- Toda escritura del launcher pasa por `SwitchTransaction` (backup → escribir → rollback si algo falla) — verificado con tests de fallo inyectado.
- Cada plataforma es un módulo independiente tras `IPlatform`.

## 4. Fases

### Fase 0 — Setup [COMPLETADA]
- [x] Solución .NET 10 (LTS): GLAS.Core + GLAS.App + GLAS.Core.Tests (xUnit)

### Fase 1 — Core sin UI [COMPLETADA]
- [x] `IPlatform` + `PlatformRegistry`
- [x] `EpicPlatform` (patrón de `EpicService.cs`, con detección de activa por hash)
- [x] `SteamPlatform` con parser/escritor VDF seguro
- [x] Gestor de snapshots (guardar, listar, renombrar, borrar)
- [x] Tests unitarios con directorios simulados

### Fase 1b — Motor genérico declarativo [COMPLETADA]
- [x] `GenericPlatform` + `PlatformDefinition` (JSON): archivos, globs, directorios, valores de registro y campos JSON como objetivos de sesión
- [x] Catálogo embebido `platforms.json` + override de usuario en `%APPDATA%\GLAS\platforms.json`
- [x] Plataformas nuevas: **Riot Games, Ubisoft Connect, Escape from Tarkov, Battle.net, EA Desktop, GOG Galaxy, Discord, Roblox** (definiciones tomadas de TcNo/accshift)
- [x] Tests del motor (capture/switch/rollback/add-new-login por cada tipo de objetivo)

### Fase 2 — Robustez y seguridad [COMPLETADA]
- [x] Switch transaccional con rollback de archivos y registro (tests con fallo inyectado)
- [x] Escritura atómica (temporal + `File.Replace`)
- [x] Cifrado opcional de snapshots con contraseña (AES-256-GCM + PBKDF2-SHA256 100k)
- [x] Export/import de todas las cuentas (`.glas`, cifrado opcional)

### Fase 3 — UI [COMPLETADA]
- [x] Ventana principal: tarjetas de cuenta con iniciales, plataforma y pill "Activa"
- [x] Switch con un clic + barra de estado con progreso
- [x] Icono en bandeja con menú por plataforma → cuenta (switch desde la bandeja)
- [x] Hotkey global Ctrl+Alt+G
- [x] Cerrar a bandeja + arranque con Windows (registro Run)

### Fase 4 — Extras [PARCIAL]
- [x] Modo "añadir cuenta": cierra sesión real, abre el login del launcher y captura con nombre
- [x] Botón "Guardar sesión actual": captura la sesión ya iniciada sin repetir login
- [x] Diálogo de alta muestra todas las plataformas (no instaladas en gris) y preselecciona la activa
- [x] Multi-idioma ES/EN (conmutable en ajustes)
- [x] Publicación verificada: exe único framework-dependent (~600 KB) en `publish/`
- [ ] Más plataformas: las de mayor riesgo quedan fuera del catálogo (Origin, Rockstar, PS Remote Play, Magic Arena — requieren resolución de carpeta de instalación o leveldb)
- [ ] Atajos de escritorio por cuenta (referencia: `shortcuts` de TcNo)
- [ ] Auto-updater + instalador (NSIS/WiX)

## 5. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Un launcher actualiza su formato de sesión y rompe el switch | Snapshots inmutables + tests de regresión por formato; logging claro del error |
| Corromper archivos del launcher a mitad de escritura | Escritura atómica (temporal + `File.Replace`) y backup previo obligatorio |
| Cuentas con datos sensibles en texto plano | Cifrado opcional con contraseña AES-256-GCM desde Ajustes |
| Baneo por "comportamiento raro" del launcher | GLAS solo mueve archivos locales; no toca APIs ni inyecta nada |

## 6. Definición de "hecho" por fase

- **Fase 1:** cambiar entre dos cuentas Epic y dos de Steam desde un test del core, sin UI, en máquina simulada. ✓ (37 tests)
- **Fase 2:** un switch interrumpido a propósito deja el launcher en su estado original. ✓ (tests de rollback)
- **Fase 3:** un usuario no técnico cambia de cuenta en un clic desde la ventana principal. ✓ (UI implementada; falta validación manual por el usuario)
- **Fase 4:** publicar un instalador y que otra persona lo use sin ayuda. (parcial: exe publicado; instalador pendiente)

## 7. Pendiente / siguiente sesión

1. **Validación manual del usuario:** switch real contra Steam, Epic, GOG y Riot en el equipo del usuario (todas instaladas; Steam en `d:/program files (x86)/steam` vía registro, Epic en `C:\Program Files\Epic Games` (64-bit), GOG en `D:\Program Files\GOG Galaxy`, Riot en `D:/Riot Games`).
2. Atajos de escritorio por cuenta.
3. Auto-updater + instalador.
4. Icono `.ico` real de marca (el actual se dibuja en runtime).
5. Git: inicializar repositorio y primer commit (hoy no se versionó).
6. Plataformas con mecanismos pendientes: Origin (legacy), Rockstar, PS Remote Play, Magic Arena (necesitan detección de carpeta de instalación o lectura de leveldb).

### Detección de instalaciones (resuelto)

El orden de detección de cada launcher es: **ruta manual de Ajustes → rutas estándar (candidatos) → atajos del Menú Inicio → registro de desinstalación de Windows** (`InstallFinder`). Esto cubre instalaciones en discos no estándar (D:, E:, ...). En Ajustes, cada plataforma muestra la ruta detectada y un campo para sobrescribirla. Epic además probea `Program Files` 64-bit antes que x86.

### Nota sobre Steam y `MostRecent`

Steam solo escribe `MostRecent` en `loginusers.vdf` cuando hay más de una cuenta recordada. GLAS resuelve la cuenta activa con la cadena `MostRecent → AutoLogin → RememberPassword → única entrada`, así que guardar sesión funciona con una sola cuenta (el error reportado venía de un VDF sin `MostRecent`).

### Archivos bloqueados durante la captura (resuelto)

Los launchers con caché de Chromium (Discord) o lockfiles (Riot) mantienen archivos de sesión abiertos mientras corren, y la captura moría con `IOException`. Ahora la captura: (1) lee con `FileShare.ReadWrite|Delete` (casi todo se deja leer así), (2) si algo sigue bloqueado, cierra los procesos del launcher y reintenta solo esos archivos, (3) lo que sigue bloqueado o desaparece (lockfiles) se omite. Verificado en la máquina real: captura OK en Steam, Epic, Riot, EA, GOG, Discord y Roblox.

### Epic en `WindowsEditor`

En esta instalación el `GameUserSettings.ini` (con el token `Data=` de sesión) vive en `Saved\Config\WindowsEditor\`, no en `Saved\Config\Windows\`. `EpicPaths.Detect` sondea ambas carpetas.

### Detección de la cuenta activa de Epic (token rotativo)

Epic re-encripta el token `Data=` de `[RememberMe]` en cada arranque del launcher, así que comparar hashes del token nunca detecta la activa. En su lugar GLAS compara los GUIDs de las secciones del ini (`[<32hex>_General]`...), que son estables por cuenta: se extraen del ini guardado de cada cuenta (funciona con cuentas capturadas antes del cambio) y se comparan con los del ini en vivo. La re-captura tras una rotación del token actualiza la cuenta en vez de duplicarla (dedup por GUIDs, con el hash del token como fallback).

### Nota sobre las plataformas genéricas

Las 8 plataformas del catálogo genérico (Riot, Ubisoft, Tarkov, Battle.net, EA Desktop, GOG Galaxy, Discord, Roblox) usan definiciones tomadas de los proyectos de referencia (TcNo/accshift), con el mismo motor transaccional y rollback que Steam/Epic. Están verificadas por tests con carpetas simuladas, **no contra instalaciones reales**: cada una debería probarse una vez con el launcher instalado antes de fiarse de ella al 100%. Si alguna falla, su entrada en `%APPDATA%\GLAS\platforms.json` se puede corregir sin recompilar.
