import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'data/auth_repository.dart';
import 'data/auth_models.dart';
import '../../core/storage/secure_storage.dart';
import '../../core/api/api_client.dart';

final secureStorageProvider = Provider<SecureStorage>((ref) => SecureStorage());

final apiClientProvider = Provider<ApiClient>((ref) {
  final storage = ref.read(secureStorageProvider);
  // Session expired: clear storage — the router will redirect to /login
  // because authStateProvider will return unauthenticated.
  return ApiClient(
    storage: storage,
    onSessionExpired: () async {
      await storage.clearSession();
      ref.invalidate(authStateProvider);
    },
  );
});

final authRepositoryProvider = Provider<AuthRepository>((ref) {
  return AuthRepository(
    apiClient: ref.read(apiClientProvider),
    storage: ref.read(secureStorageProvider),
  );
});

/// Returns an authenticated [AuthState] if session exists,
/// or an unauthenticated [AuthState] if not logged in.
final authStateProvider = FutureProvider<AuthState>((ref) async {
  final repo = ref.read(authRepositoryProvider);
  final state = await repo.hydrateFromStorage();
  // Return the hydrated state or an explicitly unauthenticated state
  return state ??
      const AuthState(
        sapUserCode: '',
        role: '',
        isAuthenticated: false,
      );
});
