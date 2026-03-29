import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'data/execution_repository.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/request_summary.dart';
import 'package:molas_supervisor_mobile/features/auth/providers.dart';

final executionRepositoryProvider = Provider<ExecutionRepository>((ref) {
  return ExecutionRepository(
    client: ref.read(apiClientProvider),
    storage: ref.read(secureStorageProvider),
  );
});

final partialFailedProvider = FutureProvider<List<RequestSummary>>((ref) {
  return ref.read(executionRepositoryProvider).listPartialFailed();
});
