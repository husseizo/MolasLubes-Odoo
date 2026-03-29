import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'data/replenishment_repository.dart';
import 'data/models/request_summary.dart';
import 'data/models/request_detail.dart';
import '../auth/providers.dart';

final replenishmentRepositoryProvider = Provider<ReplenishmentRepository>((ref) {
  return ReplenishmentRepository(
    client: ref.read(apiClientProvider),
    storage: ref.read(secureStorageProvider),
  );
});

final pendingRequestsProvider = FutureProvider<List<RequestSummary>>((ref) {
  return ref.read(replenishmentRepositoryProvider).listPending();
});

final detailProvider =
    FutureProvider.family<RequestDetail, String>((ref, requestRef) {
  return ref.read(replenishmentRepositoryProvider).getDetail(requestRef);
});
