# TokenControl — Investigación técnica

App de bandeja del sistema (system tray) de Windows que muestra cuánto uso queda del
agente de IA de Notion y avisa cuando se acaba o se reinicia. Inspirada en
[bjardon/respawken](https://github.com/bjardon/respawken) (Mac, Swift/SwiftUI) y CodexBar (Mac).

- **Fecha de investigación:** 2026-09-21
- **Plan de Notion del usuario:** Business/Enterprise (confirmado) → los medidores de allowance existen.
- **Alcance actual:** solo Notion. Diseñar para agregar Claude / Codex después.

---

## 1. ¿Se puede obtener el uso de Notion AI?

**Sí, pero NO por API pública.** La [API oficial de Notion](https://developers.notion.com) es
para páginas y bases de datos; no expone consumo de IA ni créditos.

Método viable (el que usan respawken y CodexBar): **reusar la sesión del app de escritorio**
de Notion y llamar a los **endpoints privados** que el propio cliente usa internamente.

### Auth
- La cookie **`token_v2`** del app de escritorio de Notion.
- Se envía como header `Cookie: token_v2=<valor>` + un `User-Agent` de navegador.

### Endpoints privados (`https://app.notion.com/api/v3`, todos POST)

| Endpoint | Body | Devuelve |
|---|---|---|
| `/getSpaces` | `{}` | Workspaces, email de la cuenta, `subscription_tier` / `plan_type` |
| `/getCreditRateLimitStatus` | `{"spaceId": "<id>"}` | `window` (ventana móvil 6h: `used`/`limit`/`window`), `resetsInSeconds`, `billingPeriodWindow` (mensual: `used`/`limit`/`periodStartMs`/`periodEndMs`), `status` |
| `/getAIUsageEligibilityV2` | `{"spaceId": "<id>"}` | Créditos: `premiumCredits.totalCreditBalance`, `perSource.monthlyAllocated` (`usageTotal`/`limit`) |

### ⚠️ Caveat clave
Los medidores de allowance (ventana móvil de 6h + mensual) **solo aplican a Business/Enterprise**.
En Free/Plus, `getCreditRateLimitStatus` responde `status: "not_applicable"`. Los créditos se
miden aparte. → El usuario es Business/Enterprise, así que sí tendrá medidores que mostrar.

### Nota legal
Extraer la cookie y usar endpoints privados es ingeniería inversa del cliente; técnicamente
fuera de los términos de Notion. Para uso personal leyendo datos propios el riesgo es bajo,
pero conviene tenerlo presente.

---

## 2. Viabilidad en Windows (rutas confirmadas en el equipo del usuario)

- **Cookies (SQLite):** `%APPDATA%\Notion\Partitions\notion\Network\Cookies` ✅
  (equivalente Windows del `Partitions/notion/Cookies` de Mac).
- **Clave de cifrado:** `%APPDATA%\Notion\Local State` ✅ (contiene `os_crypt.encrypted_key`).

### Diferencia técnica vs. Mac (descifrado de la cookie)
- **Mac (respawken):** password fijo del Keychain "Notion Safe Storage" + AES-CBC.
- **Windows (lo que hay que implementar):** cifrado Chromium estándar.
  1. Leer `Local State` → `os_crypt.encrypted_key` (base64).
  2. Base64-decode, quitar prefijo `DPAPI`, descifrar con **DPAPI** (`CryptUnprotectData`, usuario actual) → clave AES de 32 bytes.
  3. Cada `encrypted_value` de cookie: prefijo `v10` (3 bytes) + nonce (12 bytes) + ciphertext + tag GCM (16 bytes) → **AES-256-GCM**.
  Es el procedimiento estándar de "descifrar cookies de Chrome en Windows".

### Query SQL de la cookie (de respawken, adaptar host)
```sql
SELECT host_key, encrypted_value FROM cookies
WHERE name = 'token_v2'
ORDER BY CASE host_key
    WHEN 'app.notion.com' THEN 0
    WHEN '.app.notion.com' THEN 1
    ELSE 2 END,
    length(encrypted_value) DESC
LIMIT 4;
```

### Riesgo #1 a validar antes de construir UI
Leer + descifrar `token_v2` de punta a punta en Windows **no se ha verificado** (en el entorno
de investigación no había `sqlite3` ni Python). **Primer paso al construir:** un spike que lea
la cookie, descifre con DPAPI y llame a `/getSpaces` + `/getCreditRateLimitStatus` con números
reales. Si eso funciona, el resto es mecánico.

---

## 3. Stack recomendado: C# / .NET 8

respawken (Swift/SwiftUI) y CodexBar son solo-Mac; no reutilizables tal cual. Se copia el diseño,
no el código.

| Necesidad | Solución .NET |
|---|---|
| Ícono en bandeja + menú | `WinForms NotifyIcon` o `H.NotifyIcon` (WPF) |
| Descifrar DPAPI | `System.Security.Cryptography.ProtectedData` (nativo, cero deps) |
| Descifrar AES-GCM | `System.Security.Cryptography.AesGcm` (nativo) |
| Leer cookies SQLite | `Microsoft.Data.Sqlite` |
| HTTP | `HttpClient` |
| Notificaciones "se acabó / se reinició" | Toasts de Windows (`CommunityToolkit.WinUI.Notifications`) |
| Distribución | Un solo `.exe` self-contained |

Por qué .NET sobre alternativas: DPAPI y SQLite salen prácticamente gratis (lo más doloroso en
Rust/Go/Electron). Descartado Electron por peso (~100 MB para algo que vive en la bandeja).
Rust+Tauri o Go son válidos pero requieren más trabajo manual para DPAPI.

### Diseño a copiar de respawken
- Interfaz `IUsageProvider` por agente: empezar con `NotionProvider`, luego `ClaudeProvider` /
  `CodexProvider` sin tocar el resto.
- Polling concurrente cada ~2 min; acelerar a ~30 s cuando el uso pasa del ~90 % o falta poco
  para el reset.
- Íconos de barra apilados: relleno = utilización, color = severidad (verde/ámbar/rojo).
- Cachear la sesión: no re-descifrar la cookie en cada poll; invalidar por fecha de modificación
  de los archivos `Cookies` / `Local State`.

### Modelo de datos (de la respuesta de la API)
- **Ventana móvil (rolling):** `window.used` / `window.limit`, reset en `resetsInSeconds`, duración `window.window` (ej. "6h").
- **Mensual:** `billingPeriodWindow.used` / `.limit`, `periodStartMs` / `periodEndMs`.
- **Créditos (opcional):** `premiumCredits.totalCreditBalance`, `perSource.monthlyAllocated`.

---

## Referencias
- Código fuente Notion de respawken: `Sources/Respawken/Providers/NotionProvider.swift`
  (raw: `https://raw.githubusercontent.com/bjardon/respawken/main/Sources/Respawken/Providers/NotionProvider.swift`)
- Notion AI usage/créditos (ayuda oficial): https://www.notion.com/help/manage-ai-models-and-member-credit-spend
