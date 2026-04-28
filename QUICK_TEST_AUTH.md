# 🧪 Quick Test Guide - Auth Integration

## ⚡ Test in 5 Minutes

### Step 1: Verify Backend is Running
```powershell
# Check if API is running on ngrok tunnel
curl https://cari-unconcrete-unritually.ngrok-free.dev/health

# Or check locally
curl http://localhost:5000/health
```

### Step 2: Test Login Endpoint (PowerShell)
```powershell
$loginBody = @{
    sapUserCode = "hussein"
    password = "Modern00."
} | ConvertTo-Json

$response = Invoke-RestMethod `
    -Uri "https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/login" `
    -Method Post `
    -Headers @{ 
        "Content-Type" = "application/json"
        "X-Api-Key" = "CHANGE_ME"
    } `
    -Body $loginBody

# Display response
$response | ConvertTo-Json -Depth 10
```

**Expected Output:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "a7f3b9e1c4d2f8e9a1b3c5d7e9f1a3b5...",
  "sapUserCode": "hussein",
  "role": "Admin",
  "expiresAt": "2025-04-28T10:15:00Z"
}
```

✅ **Success Criteria:**
- Status code: 200
- All fields present: token, refreshToken, sapUserCode, role, expiresAt
- Field names are camelCase (not PascalCase)
- Role is "Admin" (based on LiquiMolyPermissions.Admins in appsettings)

### Step 3: Test Alternative Route
```powershell
$response2 = Invoke-RestMethod `
    -Uri "https://cari-unconcrete-unritually.ngrok-free.dev/auth/login" `
    -Method Post `
    -Headers @{ 
        "Content-Type" = "application/json"
    } `
    -Body $loginBody

$response2 | ConvertTo-Json -Depth 10
```

✅ **Success Criteria:**
- Same response as /api/auth/login
- Proves both routes work

### Step 4: Test Refresh Endpoint
```powershell
# Use refreshToken from login response
$refreshBody = @{
    refreshToken = $response.refreshToken
} | ConvertTo-Json

$refreshed = Invoke-RestMethod `
    -Uri "https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/refresh" `
    -Method Post `
    -Headers @{ "Content-Type" = "application/json" } `
    -Body $refreshBody

$refreshed | ConvertTo-Json -Depth 10
```

**Expected Output:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",  // NEW TOKEN
  "refreshToken": "a7f3b9e1c4d2f8e9a1b3c5d7e9f1a3b5...",  // SAME TOKEN
  "sapUserCode": "hussein",
  "role": "Admin",
  "expiresAt": "2025-04-28T10:30:00Z"  // NEW EXPIRY (15 min from now)
}
```

✅ **Success Criteria:**
- New token (different from login token)
- Same refreshToken
- All fields present with camelCase

### Step 5: Test Validate Endpoint
```powershell
Invoke-RestMethod `
    -Uri "https://cari-unconcrete-unritually.ngrok-free.dev/api/auth/validate" `
    -Headers @{ 
        "Authorization" = "Bearer $($response.token)"
    }
```

**Expected Output:**
```json
{
  "isValid": true,
  "sapUserCode": "hussein",
  "role": "Admin"
}
```

### Step 6: Test Flutter App Login

1. **Navigate to Flutter project:**
   ```powershell
   cd "F:\BAK 2025\Molaslubes\molas_supervisor_mobile"
   ```

2. **Run the app:**
   ```powershell
   flutter run -d windows
   ```

3. **Login:**
   - User Code: `hussein`
   - Password: `Modern00.`
   - Click "Login"

4. **Verify:**
   - ✅ No errors
   - ✅ Navigates to Dashboard
   - ✅ Shows "hussein" as user code
   - ✅ Shows "Admin" role badge
   - ✅ API badge shows ngrok URL

5. **Check Logs:**
   - Backend logs should show: "User hussein logged in successfully with role Admin"

---

## 🐛 Common Issues & Fixes

### Issue: "Invalid credentials"
**Cause:** Wrong username or password, or SAP server not reachable

**Fix:**
```powershell
# Verify credentials in appsettings.Development.json
code "F:\BAK 2025\Molaslubes\src\MolasLubes.Api\appsettings.Development.json"

# Check SAP section:
# "UserName": "hussein",
# "Password": "Modern00.",
# "Server": "WIN-GJGQ73V0C3K"
```

### Issue: PascalCase fields instead of camelCase
**Cause:** JSON serialization not configured correctly

**Fix:**
- Restart the API (stop debugging in VS and press F5 again)
- Verify Program.cs has the JSON configuration added
- Clear browser cache if testing in Swagger

### Issue: 404 Not Found
**Cause:** Route doesn't exist or API not running

**Fix:**
```powershell
# Check if API is running
curl https://cari-unconcrete-unritually.ngrok-free.dev/swagger

# Verify ngrok tunnel is active
# If ngrok stopped, restart it and update env.dart
```

### Issue: CORS error (Flutter web only)
**Cause:** CORS not configured

**Fix:** Add to Program.cs (before `app.Run()`):
```csharp
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());
```

---

## ✅ Checklist

### Backend Verification
- [ ] API is running (check swagger at /swagger)
- [ ] POST /api/auth/login returns 200 with all fields
- [ ] POST /auth/login works (alternative route)
- [ ] Response fields are camelCase (not PascalCase)
- [ ] POST /api/auth/refresh returns new token
- [ ] GET /api/auth/validate returns user info
- [ ] Logs show "User logged in successfully"

### Flutter App Verification
- [ ] App launches without errors
- [ ] Login screen displays ngrok URL badge
- [ ] Can enter credentials
- [ ] Login button triggers API call
- [ ] Success navigates to Dashboard
- [ ] Dashboard shows user code ("hussein")
- [ ] Dashboard shows role badge ("Admin")
- [ ] Logout button works
- [ ] After logout, redirects to Login

---

## 📊 Test Results Template

```
Date: _______________
Tester: _______________

Backend Tests:
[ ] POST /api/auth/login - Status: ___ (expected 200)
[ ] POST /auth/login - Status: ___ (expected 200)
[ ] Field names camelCase: Yes / No
[ ] POST /api/auth/refresh - Status: ___ (expected 200)
[ ] GET /api/auth/validate - Status: ___ (expected 200)

Flutter App Tests:
[ ] App launches: Yes / No
[ ] Login with hussein/Modern00.: Success / Fail
[ ] Dashboard displays: Yes / No
[ ] User code shown: _______________
[ ] Role shown: _______________
[ ] Logout works: Yes / No

Issues Found:
_________________________________________________
_________________________________________________

Notes:
_________________________________________________
_________________________________________________
```

---

## 🎯 Success Definition

**Backend is ready when:**
- All 5 backend tests pass ✅
- Logs show successful authentication ✅
- Response fields match Flutter expectations exactly ✅

**Integration is complete when:**
- Flutter app can login successfully ✅
- Dashboard shows correct user info ✅
- Logout and re-login works ✅
- No errors in console/logs ✅

---

## 🚀 Next Actions After Tests Pass

1. **Commit Backend Changes:**
   ```powershell
   git add .
   git commit -m "feat: Add SAP authentication API with JWT tokens

   - Created AuthController and AuthAltController for dual route support
   - Implemented login, refresh, logout, validate endpoints
   - Configured JSON camelCase serialization for Flutter compatibility
   - Updated models to match frontend contract exactly
   - All endpoints tested and working"
   ```

2. **Connect Real Data in Flutter:**
   - Update replenishment list to use API (replace mock data)
   - Create replenishment detail screen
   - Implement approval/rejection flows

3. **Production Deployment:**
   - Update JWT secret (use 64+ char random string)
   - Move secrets to environment variables
   - Deploy to production server
   - Update Flutter env.dart with production URL

---

**Test Duration:** ~5 minutes  
**Expected Result:** 100% pass rate ✅
