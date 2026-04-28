# SAP User Role Configuration

## Overview
The MolasLubes API determines user roles from SAP Business One user master data (`OUSR` table).

**Role Priority:**
1. **Custom UDF** (`U_MolasRole`) - highest priority
2. **Department** field - fallback
3. **Default** - "Viewer" if neither is set

## Available Roles
- `Admin` - Full access to all features
- `Supervisor` - Can approve/reject replenishment requests
- `Planner` - Can create replenishment drafts and submit for approval
- `Executor` - Can execute approved transfers
- `Viewer` - Read-only access

---

## ✅ Quick Fix: Give "hussein" Admin Access

### Option A: Using Custom Role Field (Recommended)

**Step 1:** Check if `U_MolasRole` field exists:
```sql
SELECT * FROM CUFD 
WHERE TableID = 'OUSR' AND AliasID = 'MolasRole'
```

**Step 2a:** If field doesn't exist, create it via SAP GUI:
1. Open SAP Business One
2. Go to **Tools → Customization Tools → User-Defined Fields - Management**
3. Select Table: **Users (OUSR)**
4. Click **Add**
   - Name: `MolasRole`
   - Description: `MolasLubes Role`
   - Type: `Alphanumeric`
   - Size: `20`
   - Valid Values: `Viewer`, `Planner`, `Executor`, `Supervisor`, `Admin`
5. Click **Add** then **Update**

**Step 2b:** Or create via SQL (if supported):
```sql
-- This may require SAP SDK/DI API; direct SQL creation of UDFs is not recommended
```

**Step 3:** Set hussein's role to Admin:
```sql
UPDATE OUSR 
SET U_MolasRole = 'Admin' 
WHERE USER_CODE = 'hussein'
```

**Step 4:** Verify:
```sql
SELECT USER_CODE, DEPARTMENT, U_MolasRole 
FROM OUSR 
WHERE USER_CODE = 'hussein'
```

---

### Option B: Using Department Field (Quick Alternative)

If you can't create the UDF immediately, use the department field:

```sql
-- Update hussein's department to trigger Admin role
UPDATE OUSR 
SET DEPARTMENT = 'ADMIN' 
WHERE USER_CODE = 'hussein'
```

**Verify:**
```sql
SELECT USER_CODE, DEPARTMENT 
FROM OUSR 
WHERE USER_CODE = 'hussein'
```

---

## Department → Role Mapping

The API maps SAP departments to roles as follows:

| SAP Department | API Role |
|---------------|----------|
| `ADMIN`, `IT` | `Admin` |
| `WAREHOUSE`, `LOGISTICS` | `Executor` |
| `PURCHASING`, `PLANNING` | `Planner` |
| `MANAGEMENT`, `SUPERVISOR` | `Supervisor` |
| *(any other)* | `Viewer` |

---

## Testing

After updating SAP, test the new role:

**PowerShell:**
```powershell
$body = @{
    sapUserCode = "hussein"
    password = "YOUR_PASSWORD"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/login" `
    -Method Post `
    -Body $body `
    -ContentType "application/json"

# Should now show role: "Admin"
$response | ConvertTo-Json -Depth 5
```

**curl:**
```bash
curl -X POST https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "sapUserCode": "hussein",
    "password": "YOUR_PASSWORD"
  }'
```

**Expected Response:**
```json
{
  "token": "eyJhbGc...",
  "refreshToken": "abc123...",
  "sapUserCode": "hussein",
  "role": "Admin",  ← Should now be "Admin"
  "expiresAt": "2025-06-03T08:04:09Z"
}
```

---

## Code Reference

**Location:** `src/MolasLubes.Infrastructure/Security/SapUserAuthService.cs`

**Key Methods:**
- `ValidateCredentialsAsync()` - Authenticates user and retrieves role
- `GetUserRole()` - Reads `U_MolasRole` or `DEPARTMENT` from SAP
- `MapDepartmentToRole()` - Fallback department mapping
- `IsValidRole()` - Validates role string

**SQL Query Used:**
```sql
SELECT TOP 1 
    U.USER_CODE,
    U.DEPARTMENT,
    ISNULL(U.U_MolasRole, '') AS CustomRole
FROM OUSR U
WHERE U.USER_CODE = 'hussein'
```

---

## Notes

- **No API restart required** - Role is read from SAP on every login
- **Custom UDF takes precedence** over department mapping
- **Case-insensitive** - "Admin", "admin", "ADMIN" all work
- **Invalid roles default to "Viewer"** - typos won't break auth
- **Changes are immediate** - Just re-login after updating SAP

---

## Bulk Role Assignment

To update multiple users at once:

```sql
-- Make all IT department users Admins
UPDATE OUSR 
SET U_MolasRole = 'Admin' 
WHERE DEPARTMENT = 'IT'

-- Make specific users Supervisors
UPDATE OUSR 
SET U_MolasRole = 'Supervisor' 
WHERE USER_CODE IN ('manager1', 'manager2', 'supervisor1')

-- Make warehouse users Executors
UPDATE OUSR 
SET U_MolasRole = 'Executor' 
WHERE DEPARTMENT IN ('WAREHOUSE', 'LOGISTICS')
```
