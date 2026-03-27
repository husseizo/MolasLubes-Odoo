import 'package:molas_supervisor_mobile/core/api/api_client.dart';
import 'package:molas_supervisor_mobile/core/storage/secure_storage.dart';
import 'package:molas_supervisor_mobile/features/auth/data/auth_models.dart';

class AuthRepository {
  const AuthRepository({
    required ApiClient apiClient,
    required SecureStorage storage,
  })  : _apiClient = apiClient,
        _storage = storage;

  final ApiClient _apiClient;
  final SecureStorage _storage;

  /// Authenticates with SAP user code + password.
  /// On success, persists the session to secure storage.
  Future<LoginResponse> login({
    required String sapUserCode,
    required String password,
  }) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/api/auth/login',
      data: LoginRequest(
        sapUserCode: sapUserCode,
        password: password,
      ).toJson(),
    );

    final loginResponse = LoginResponse.fromJson(response.data!);

    await _storage.saveSession(
      jwtToken: loginResponse.token,
      refreshToken: loginResponse.refreshToken,
      sapUserCode: loginResponse.sapUserCode,
      role: loginResponse.role,
    );

    return loginResponse;
  }

  /// Restores session from secure storage. Returns null if not logged in.
  Future<AuthState?> hydrateFromStorage() async {
    final sapUserCode = await _storage.sapUserCode;
    final role = await _storage.sapRole;

    if (sapUserCode == null || role == null) return null;

    return AuthState(
      sapUserCode: sapUserCode,
      role: role,
      isAuthenticated: true,
    );
  }

  /// Clears all stored credentials.
  Future<void> logout() => _storage.clearSession();
}
