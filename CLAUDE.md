# TokenControl

App de bandeja del sistema (Windows) que muestra cuánto uso queda del agente de IA de Notion
y avisa cuando se acaba o se reinicia. Investigación completa en [docs/RESEARCH.md](docs/RESEARCH.md).

## Cómo trabajar con el usuario

- Al usuario le importa **el resultado, no la ingeniería**. No lee ni edita el código; Claude
  tiene control total de las decisiones técnicas (arquitectura, librerías, estructura, refactors).
- No preguntes sobre detalles de implementación: decide y avanza. Pregunta solo por
  comportamiento visible (qué muestra la app, cuándo avisa, cómo se ve) o por cosas que solo el
  usuario puede hacer (abrir la app, confirmar que los números coinciden con Notion).
- Reporta en español y en términos de producto: qué hace ahora la app, qué falta, qué necesitas
  de él. Nada de volcados de código ni jerga salvo que la pida.
- "Terminado" significa verificado: compilado, ejecutado y comprobado con datos reales.
  Si algo no se pudo verificar, dilo explícitamente.

## Stack

- **C# / .NET 10** (LTS). SDK instalado en `C:\Program Files\dotnet`. Target `net10.0-windows`.
- Bandeja: WinForms `NotifyIcon`. Descifrado: `ProtectedData` (DPAPI) + `AesGcm` (nativos).
- SQLite: `Microsoft.Data.Sqlite`. HTTP: `HttpClient`. Notificaciones: toasts de Windows.
- Distribución: un solo `.exe` self-contained (`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`).
- Minimiza dependencias NuGet; prefiere lo que trae .NET.

## Arquitectura

- Interfaz `IUsageProvider` por agente. Hoy solo `NotionProvider`; Claude/Codex se agregarán
  después sin tocar el resto. Mantén la UI desacoplada de los proveedores.
- Polling cada ~2 min; ~30 s cuando el uso pasa de ~90 % o el reset está cerca.
- Cachea la cookie descifrada; invalida por fecha de modificación de `Cookies` / `Local State`.
- Los endpoints de Notion son privados y pueden cambiar: parsea de forma tolerante y muestra un
  estado de error claro en la bandeja en vez de crashear.

## Seguridad

- **Nunca** escribas `token_v2` (ni la clave AES) en logs, archivos, commits ni salida de consola.
  Si hace falta depurar, muestra solo longitud o los primeros 4 caracteres.
- Abre la base de cookies en modo solo lectura (o copia a un temporal): Notion puede tenerla bloqueada.
- Solo se hacen peticiones a Notion (`app.notion.com` o `www.notion.so`, según el dominio de la
  cookie; la app de escritorio de este equipo usa `www.notion.so`). No envíes datos a ningún otro servicio.

## Código

- Código, identificadores y comentarios en inglés; textos visibles de la UI en español.
- Nullable habilitado, warnings como errores. Comentarios solo donde el "por qué" no es obvio.
- Pruebas unitarias para la lógica pura (descifrado, parseo de respuestas, cálculo de severidad);
  la integración con Notion se verifica ejecutando contra la cuenta real.

## Git

- Rama `main`, remoto `origin` (https://github.com/angyve/TokenControl.git).
- Commits pequeños y descriptivos (en inglés). Haz push cuando un hito funcione y esté verificado.

## Comandos

- Compilar: `dotnet build` (desde la raíz; el SDK está en `C:\Program Files\dotnet`, puede no estar en el PATH).
- Prueba de punta a punta contra Notion: `dotnet run --project tools/TokenControl.Spike [-- --raw]`.
