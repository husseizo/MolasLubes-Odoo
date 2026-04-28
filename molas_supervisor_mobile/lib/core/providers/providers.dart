import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:molas_supervisor_mobile/core/api/api_client.dart';
import 'package:molas_supervisor_mobile/core/storage/secure_storage.dart';
import 'package:molas_supervisor_mobile/features/auth/data/auth_repository.dart';
import 'package:molas_supervisor_mobile/features/auth/data/auth_models.dart';

/// Provides the SecureStorage singleton instance
final secureStorageProvider = Provider<SecureStorage>((ref) {
  return const SecureStorage();
});

/// Provides the ApiClient singleton instance
final apiClientProvider = Provider<ApiClient>((ref) {
  return ApiClient();
});

/// Provides the AuthRepository instance
final authRepositoryProvider = Provider<AuthRepository>((ref) {
  final apiClient = ref.watch(apiClientProvider);
  final storage = ref.watch(secureStorageProvider);
  
  return AuthRepository(
    apiClient: apiClient,
    storage: storage,
  );
});

/// Provides the current authentication state
/// 
/// This provider loads the auth state on startup by checking secure storage
/// for persisted tokens. It returns null while loading, an AuthState if 
/// a session exists, or null if not authenticated.
final authStateProvider = FutureProvider<AuthState?>((ref) async {
  final authRepo = ref.watch(authRepositoryProvider);
  
  try {
    // Attempt to hydrate session from secure storage
    final state = await authRepo.hydrateSession();
    return state;
  } catch (e) {
    // If hydration fails (no tokens or invalid), user is not authenticated
    return null;
  }
});

/// Provides a synchronous auth state for quick checks
/// 
/// Returns null if still loading, otherwise returns the auth state.
/// Use this when you need immediate access without awaiting.
final authStateSyncProvider = Provider<AuthState?>((ref) {
  final asyncState = ref.watch(authStateProvider);
  return asyncState.when(
    data: (state) => state,
    loading: () => null,
    error: (_, __) => null,
  );
});

/// Helper provider to check if user is authenticated
final isAuthenticatedProvider = Provider<bool>((ref) {
  final authState = ref.watch(authStateSyncProvider);
  return authState?.isAuthenticated ?? false;
});

/// Helper provider to get current user role
final currentUserRoleProvider = Provider<String?>((ref) {
  final authState = ref.watch(authStateSyncProvider);
  return authState?.role;
});

/// Helper provider to get current SAP user code
final currentUserCodeProvider = Provider<String?>((ref) {
  final authState = ref.watch(authStateSyncProvider);
  return authState?.sapUserCode;
});
