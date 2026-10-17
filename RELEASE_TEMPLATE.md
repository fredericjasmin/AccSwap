# Versionado y Release Notes — AccSwap

## Esquema de versiones (SemVer)

Formato: `vMAYOR.MENOR.PATCH`, p. ej. `v0.4.0`.

| Fase | Versiones | Cuándo pasa |
|---|---|---|
| Alpha (actual) | `0.x.y` | Mientras la app no esté probada a fondo |
| Beta | `1.0.0` en adelante | Cuando el usuario la considere estable |
| Estable | `2.0.0` en adelante | Uso público generalizado |

Cómo subir el número dentro de la fase actual:

- **PATCH** (`0.4.1`): solo correcciones de bugs, sin nada visible nuevo.
- **MINOR** (`0.5.0`): cualquier novedad visible para el usuario (plataformas, ajustes, UI).
- **MAJOR**: cambio de fase (alpha -> beta -> estable).

La versión vive SOLO en `<Version>` de `AccSwap.App/AccSwap.App.csproj`; nunca
duplicarla a mano en otros sitios.

## Política de releases en GitHub

1. **Siempre una sola release visible**: al publicar una nueva, se borra la
   anterior (release + tag). La página queda limpia con un solo archivo
   `AccSwap-vX.Y.Z.exe` para descargar, y el historial real vive en los commits.
2. **Nunca marcar como pre-release**: el actualizador usa
   `releases/latest` y GitHub excluye las pre-releases de ese endpoint.
   La fase (alpha/beta) se indica en el título, no en la casilla.
3. **Siempre marcar como "Latest"** con `prerelease:false`.
4. El exe adjunto siempre se llama `AccSwap-vX.Y.Z.exe` y se genera con:
   ```
   dotnet publish AccSwap.App/AccSwap.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
   ```
5. La release debe incluir al final un bloque **Historial de versiones**
   (lista acumulativa de qué trajo cada versión) para que quien descarga
   vea todo lo cambiado sin necesidad de releases antiguas.

---

# Plantilla de Release Notes

Copiar y rellenar para cada release. Si un apartado no aplica, borrarlo;
si la release es pequeña, basta con "Novedades" en una o dos líneas.

## AccSwap v{X.Y.Z}

Fecha: YYYY-MM-DD

### Novedades
- ...

### Correcciones
- ...

### Plataformas
- (Cambios en detección, nuevas plataformas del catálogo, etc.)

### Notas
- (Advertencias, migraciones, cosas conocidas que no se arreglaron.)
- Mientras el exe no esté firmado: recordar el aviso de SmartScreen
  ("Más información -> Ejecutar de todas formas").

### Historial de versiones
- **v{X.Y.Z}** — esta release.
- **v{X.Y.(Z-1)}** — (una línea con lo esencial; copiar el bloque de la
  release anterior y añadirlo debajo).

---

## Versión mínima (cuando no hay casi nada)

## AccSwap v{X.Y.Z}

Fecha: YYYY-MM-DD

- Mantenimiento y correcciones menores.

### Historial de versiones
- **v{X.Y.Z}** — esta release.
- (bloques anteriores)

Más detalles en [los commits](https://github.com/fredericjasmin/AccSwap/commits/main).
