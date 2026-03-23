# Mejoras de Seguridad — CASATIC Directorio 2026

## ¿Qué se hizo?

Se aplicó una reestructuración de seguridad a nivel profesional (senior) para proteger las credenciales y datos sensibles del proyecto, siguiendo las mejores prácticas de la industria y las recomendaciones del OWASP Top 10.

---

## Problemas que existían antes

| Problema | Riesgo |
|----------|--------|
| Contraseñas de PostgreSQL hardcodeadas en `docker-compose.yml` | Cualquier persona con acceso al repo ve las credenciales de la BD |
| JWT Secret Key visible en `appsettings.json` | Un atacante puede generar tokens falsos y suplantar al admin |
| `backup.sql` con datos reales en Git | Expone emails, hashes de contraseñas y estructura completa de la BD |
| Credenciales del admin (`Admin123!`) en `schema.sql` y documentación | Acceso directo al panel de administración |
| `appsettings.Development.json` con connection string real | Acceso a la base de datos desde cualquier máquina |

**Impacto potencial:** Cualquiera que clone el repositorio público obtiene acceso completo a la base de datos, al panel de administración y puede generar tokens JWT válidos.

---

## Cambios aplicados

### 1. Variables de entorno con `.env`

**Antes:**
```yaml
# docker-compose.yml — TODO PÚBLICO EN GITHUB
POSTGRES_PASSWORD: casatic2026
Jwt__Key: "CASATIC-2026-Docker-SecretKey..."
```

**Después:**
```yaml
# docker-compose.yml — Sin secretos, seguro para GitHub
POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
Jwt__Key: ${JWT_KEY}
```

Los valores reales ahora viven en un archivo `.env` que **nunca se sube a Git**. Se incluye un `.env.example` como plantilla para que los compañeros sepan qué variables configurar.

**Seguridad que brinda:**
- Las credenciales no están en el historial de Git
- Cada desarrollador puede tener sus propias credenciales locales
- En producción se usan variables de entorno del servidor, no archivos

---

### 2. `.gitignore` nivel senior

**Archivos que ahora se ignoran:**

| Patrón | Qué protege |
|--------|-------------|
| `.env`, `.env.local`, `.env.production` | Credenciales y secretos |
| `*.sql` (excepto `schema.sql`) | Backups con datos reales de la BD |
| `logs/`, `*.log` | Información interna del servidor |
| `wwwroot/logos/*` | Archivos subidos por usuarios |
| `docker-compose.override.yml` | Configuraciones locales con secretos |

**Seguridad que brinda:**
- Previene subidas accidentales de archivos sensibles
- Los nuevos archivos de backup o logs nunca se subirán por error
- Protege datos de usuarios (logos, archivos subidos)

---

### 3. `appsettings.json` con placeholders

**Antes:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;...Password=casatic2026..."
  },
  "Jwt": {
    "Key": "CASATIC-2026-SuperSecretKey-Cambiame..."
  }
}
```

**Después:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "REEMPLAZADO_POR_VARIABLE_DE_ENTORNO"
  },
  "Jwt": {
    "Key": "REEMPLAZADO_POR_VARIABLE_DE_ENTORNO"
  }
}
```

Los valores reales los inyecta Docker a través de las variables de entorno definidas en el `docker-compose.yml`, que a su vez las lee del `.env`.

**Seguridad que brinda:**
- El código fuente no contiene ninguna credencial real
- Separación clara entre configuración y código (principio de 12-Factor App)
- Cada entorno (desarrollo, staging, producción) usa sus propias credenciales

---

### 4. Archivos sensibles removidos del tracking

Se ejecutó `git rm --cached` en los siguientes archivos:

- `backup.sql` — Contenía los 20 registros de empresas con datos completos
- `backup_fixed.sql` — Copia anterior del backup
- `RESTAURAR_BASE_DE_DATOS.txt` — Contenía credenciales de admin y pgAdmin
- `docker-compose.yml` (frontend) — Contenía credenciales duplicadas

**Seguridad que brinda:**
- Estos archivos siguen existiendo en disco para uso local
- Ya no se suben a GitHub en futuros commits
- Los compañeros reciben los archivos sensibles por canales seguros (no Git)

---

### 5. `schema.sql` limpio

Se removieron las credenciales reales de los comentarios del esquema de base de datos.

**Antes:** `CREATE USER casatic WITH PASSWORD 'casatic2026'`
**Después:** `CREATE USER <POSTGRES_USER> WITH PASSWORD '<POSTGRES_PASSWORD>'`

**Seguridad que brinda:**
- El esquema de la BD es seguro para documentación y revisión de código
- No revela credenciales ni siquiera en comentarios

---

## Resumen de protección por capa

```
┌─────────────────────────────────────────────────┐
│                    GITHUB                        │
│  ✅ docker-compose.yml  → solo ${VARIABLES}     │
│  ✅ appsettings.json    → solo placeholders     │
│  ✅ schema.sql          → sin credenciales      │
│  ✅ .env.example        → plantilla sin valores │
│  ❌ .env                → NO se sube            │
│  ❌ backup.sql          → NO se sube            │
│  ❌ logs/               → NO se sube            │
└─────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────┐
│               MÁQUINA LOCAL                      │
│  📁 .env           → credenciales reales        │
│  📁 backup.sql     → datos de la BD             │
│  📁 logs/          → registros del servidor     │
│  📁 wwwroot/logos/ → archivos subidos           │
└─────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────┐
│            DOCKER (en ejecución)                 │
│  🔒 Variables de entorno inyectadas desde .env  │
│  🔒 Credenciales solo existen en memoria        │
│  🔒 No hay archivos de config con secretos      │
└─────────────────────────────────────────────────┘
```

---

## Vulnerabilidades OWASP que se mitigan

| OWASP Top 10 | Vulnerabilidad | Cómo se mitiga |
|:---:|---|---|
| A01 | Broken Access Control | Credenciales del admin ya no están públicas |
| A02 | Cryptographic Failures | JWT Secret Key fuera del código fuente |
| A05 | Security Misconfiguration | `.gitignore` previene exposición accidental |
| A07 | Identification & Authentication Failures | Contraseñas de BD no están en repositorio público |
| A08 | Software & Data Integrity Failures | Backups separados del control de versiones |
| A09 | Security Logging & Monitoring Failures | Logs excluidos de Git (contienen IPs y actividad) |

---

## Instrucciones para compañeros de equipo

### Primera vez (clonar y configurar)

```powershell
# 1. Clonar el repo
git clone -b desarrollo https://github.com/CASATIC2026/...Backend.git backend

# 2. Crear archivo .env a partir del ejemplo
cd backend
Copy-Item .env.example .env

# 3. Editar .env con las credenciales reales
# (pedir las credenciales al responsable del proyecto por un canal seguro)
notepad .env

# 4. Levantar Docker
docker compose up -d --build
```

### Reglas de seguridad del equipo

1. **NUNCA** subir el archivo `.env` a Git
2. **NUNCA** escribir contraseñas en el código fuente
3. **NUNCA** compartir credenciales por chat público — usar canal privado
4. **SIEMPRE** revisar `git status` antes de hacer commit
5. **SIEMPRE** usar variables de entorno para secretos
6. Si necesitas agregar una nueva credencial, agregarla al `.env` y al `.env.example` (sin el valor real)

---

*Documento generado el 19 de marzo de 2026*
*Proyecto: Directorio Interactivo CASATIC 2026*
