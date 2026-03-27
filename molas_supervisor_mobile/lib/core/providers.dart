import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'package:molas_supervisor_mobile/core/api/api_client.dart';
import 'package:molas_supervisor_mobile/core/storage/secure_storage.dart';
import 'package:molas_supervisor_mobile/features/auth/data/auth_repository.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/replenishment_repository.dart';

final secureStorageProvider = Provider<SecureStorage>((ref) {
  return SecureStorage();
});

final apiClientProvider = Provider<ApiClient>((ref) {
  final storage = ref.watch(secureStorageProvider);

  return ApiClient(
    storage: storage,
    onSessionExpired: () {},
  );
});

final authRepositoryProvider = Provider<AuthRepository>((ref) {
  return AuthRepository(
    apiClient: ref.watch(apiClientProvider),
    storage: ref.watch(secureStorageProvider),
  );
});

final replenishmentRepositoryProvider = Provider<ReplenishmentRepository>((ref) {
  return ReplenishmentRepository(
    apiClient: ref.watch(apiClientProvider),
  );
});
