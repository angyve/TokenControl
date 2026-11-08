# TokenControl

App de bandeja del sistema (Windows) que muestra cuánto uso queda del plan de **Claude**
(sesión de 5 h, límite semanal y uso extra, como en claude.ai → Configuración → Uso) y avisa
cuando se acaba o se reinicia. Empezó midiendo Notion AI; ese código sigue en `Core/Notion`
pero la app ya no lo usa. Investigación de Notion en [docs/RESEARCH.md](docs/RESEARCH.md).

## Cómo trabajar con el usuario

- Al usuario le importa **el resultado, no la ingeniería**. No lee ni edita el código; Claude
  tiene control total de las decisiones técnicas (arquitectura, librerías, estructura, refactors).
- No preguntes sobre detalles de implementación: decide y avanza. Pregunta solo por
  comportamiento visible (qué muestra la app, cuándo avisa, cómo se ve) o por cosas que solo el
  usuario puede hacer (abrir la app, confirmar que los números coinciden con claude.ai).
- Reporta en español y en términos de producto: qué hace ahora la app, qué falta, qué necesitas
  de él. Nada de volcados de código ni jerga salvo que la pida.
- "Terminado" significa verificado: compilado, ejecutado y comprobado con datos reales.
  Si algo no se pudo verificar, dilo explícitamente.

## Stack

- **C# / .NET 10** (LTS). SDK instalado en `C:\Program Files\dotnet`. Target `net10.0-windows`.
- Bandeja: WinForms `NotifyIcon`. Inicio de sesión y consultas: WebView2 (`Microsoft.Web.WebView2`).
- Cifrado: `ProtectedData` (DPAPI). Notificaciones: globos de `NotifyIcon` (Windows los muestra como toasts).
- Distribución: un solo `.exe` self-contained y comprimido (~50 MB), perfil `win-x64`.
- Minimiza dependencias NuGet; prefiere lo que trae .NET.

## Arquitectura

- **Sesión propia:** el usuario inicia sesión en claude.ai dentro de TokenControl (ventana WebView2,
  `LoginForm`); la cookie `sessionKey` se guarda cifrada con DPAPI en
  `%LOCALAPPDATA%\TokenControl\claude-session.dat`. No depende de Chrome ni de las apps de Claude.
- **Consultas vía WebView2 invisible** (`WebViewClaudeTransport`): Cloudflare desafía a `HttpClient`
  en cuanto lleva la cookie de sesión, así que las peticiones se hacen con `fetch()` dentro de un
  WebView2 oculto que comparte perfil con el login. `HttpClaudeTransport` solo se usa para validar el login.
- Endpoints privados de claude.ai: `/api/organizations` (plan) y `/api/organizations/{id}/usage`
  (`five_hour`, `seven_day`, `seven_day_*`, `extra_usage`; `utilization` en 0–100). Pueden cambiar:
  `ClaudeUsageParser` es tolerante y muestra límites desconocidos con título genérico.
- Interfaz `IUsageProvider` por agente (`ClaudeProvider`; `NotionProvider` queda disponible).
  Mantén la UI desacoplada de los proveedores.
- Polling cada ~2 min; ~30 s cuando el uso pasa de ~90 % o el reset está cerca.

## Seguridad

- **Nunca** escribas `sessionKey` ni `token_v2` en logs, archivos, commits ni salida de consola.
  Si hace falta depurar, muestra solo longitud o los primeros 4 caracteres.
- `activity.log` registra resultados de cada consulta; nunca tokens ni cookies.
- Solo se hacen peticiones a `claude.ai`. No envíes datos a ningún otro servicio.

## Código

- Código, identificadores y comentarios en inglés; textos visibles de la UI en español.
- Nullable habilitado, warnings como errores. Comentarios solo donde el "por qué" no es obvio.
- Pruebas unitarias para la lógica pura (parseo de respuestas, cálculo de severidad y ritmo);
  la integración con claude.ai se verifica ejecutando contra la cuenta real.

## Git

- Rama `main`, remoto `origin` (https://github.com/angyve/TokenControl.git).
- Commits pequeños y descriptivos (en inglés). Haz push cuando un hito funcione y esté verificado.

## Comandos

El SDK está en `C:\Program Files\dotnet` y puede no estar en el PATH.

- Compilar: `dotnet build` · Tests: `dotnet test`
- Publicar el `.exe`: `dotnet publish src/TokenControl.App -p:PublishProfile=win-x64` → `dist/TokenControl.exe`
- Instalar en el equipo del usuario: cerrar `TokenControl.exe`, copiar a
  `%LOCALAPPDATA%\Programs\TokenControl\` y volver a abrirlo ("Iniciar con Windows" apunta a esa ruta).
- Diagnóstico: `%LOCALAPPDATA%\TokenControl\activity.log` (resultado de cada consulta, sin secretos).
- `tools/TokenControl.Spike` es la herramienta de prueba de Notion (de la etapa anterior).
