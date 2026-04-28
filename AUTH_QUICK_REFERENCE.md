# 🎯 Quick Reference - Auth Endpoints

## Available Routes

### Login
```
POST /api/auth/login
POST /auth/login
```

### Refresh
```
POST /api/auth/refresh
POST /auth/refresh
```

### Logout
```
POST /api/auth/logout
POST /auth/logout
```

### Validate
```
GET /api/auth/validate
GET /auth/validate
```

---

## Request/Response Examples

### Login
**Request:**
```json
{
  "sapUserCode": "hussein",
  "password": "Modern00."
}
```

**Response (200 OK):**
```json
{
  "token": "eyJhbGc...",
  "refreshToken": "a7f3b9e...",
  "sapUserCode": "hussein",
  "role": "Admin",
  "expiresAt": "2025-04-28T10:15:00Z"
}
```

### Refresh
**Request:**
```json
{
  "refreshToken": "a7f3b9e..."
}
```

**Response (200 OK):**
```json
{
  "token": "eyJhbGc...",
  "refreshToken": "a7f3b9e...",
  "sapUserCode": "hussein",
  "role": "Admin",
  "expiresAt": "2025-04-28T10:30:00Z"
}
```

---

## Headers

### All Requests
```
Content-Type: application/json
Accept: application/json
X-Api-Key: <your-key>  (optional)
```

### Protected Endpoints
```
Authorization: Bearer <access-token>
```

---

## Status Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 401 | Invalid credentials / Invalid token |
| 422 | Validation error |
| 500 | Server error |

---

## Test Commands

### PowerShell
```powershell
# Login
$body = @{ sapUserCode = "hussein"; password = "Modern00." } | ConvertTo-Json
Invoke-RestMethod -Uri "https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/login" -Method Post -Headers @{ "Content-Type" = "application/json" } -Body $body

# Refresh
$refreshBody = @{ refreshToken = "<token>" } | ConvertTo-Json
Invoke-RestMethod -Uri "https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/refresh" -Method Post -Headers @{ "Content-Type" = "application/json" } -Body $refreshBody
```

### curl
```bash
# Login
curl -X POST https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"sapUserCode":"hussein","password":"Modern00."}'

# Refresh
curl -X POST https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<token>"}'
```

---

## Files Modified/Created

### Created
- `src/MolasLubes.Api/Controllers/AuthAltController.cs`
- `BACKEND_AUTH_API_COMPLETE.md`
- `QUICK_TEST_AUTH.md`
- `BACKEND_AUTH_SUMMARY.md`

### Modified
- `src/MolasLubes.Api/Controllers/AuthController.cs`
- `src/MolasLubes.Api/Models/Auth/AuthModels.cs`
- `src/MolasLubes.Api/Program.cs`

---

## Test Credentials

**User:** hussein  
**Password:** Modern00.  
**Expected Role:** Admin

---

## Configuration

**JWT Secret:** ≥ 32 characters  
**Access Token:** 15 minutes  
**Refresh Token:** 7 days  
**JSON Format:** camelCase

---

**Status:** ✅ Ready for testing
