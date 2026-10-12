# Plantilla de Release Notes — GLAS

Copiar y rellenar para cada release. Si un apartado no aplica, borrarlo;
si la release es pequeña, basta con "Novedades" en una o dos líneas.

Para releases preliminares: marcar la casilla **"Set as a pre-release"**
en GitHub. Tag con el mismo número que `<Version>` del csproj
(p. ej. `v0.2.0`), con sufijo `-alpha` / `-beta` si es preliminar
(p. ej. `v0.2.0-alpha`).

Adjuntar siempre: `GLAS-vX.Y.Z.exe` (generado con
`dotnet publish GLAS.App/GLAS.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish`).

---

## GLAS v{X.Y.Z}

Fecha: YYYY-MM-DD

### Novedades
- ...

### Correcciones
- ...

### Plataformas
- (Cambios en detección, nuevas plataformas del catálogo, etc.)

### Notas
- (Advertencias, migraciones, cosas conocidas que no se arreglaron.)
- Mientras el exe no esté firmado: recordar el aviso de SmartScreen ("Más información -> Ejecutar de todas formas").

---

## Versión mínima (cuando no hay casi nada)

## GLAS v{X.Y.Z}

Fecha: YYYY-MM-DD

- Mantenimiento y correcciones menores.

Más detalles en [los commits desde la versión anterior](https://github.com/fredericjasmin/Game-Launcher-Account-Switcher/commits/main).
