import 'package:molas_supervisor_mobile/core/api/api_client.dart';
import 'package:molas_supervisor_mobile/core/storage/secure_storage.dart';
import 'models/request_summary.dart';
import 'models/request_detail.dart';

class ReplenishmentRepository {
  ReplenishmentRepository({
    required ApiClient client,
    required SecureStorage storage,
  })  : _client = client,
        _storage = storage;

  final ApiClient _client;
  final SecureStorage _storage;

  Future<String?> _actorCode() => _storage.sapUserCode;

  Future<List<RequestSummary>> listPending() async {
    final actor = await _actorCode();
    final resp = await _client.get(
      '/api/admin/liquimoly/replenishment',
      queryParameters: {
        'actorSapUserCode': actor,
        'status': 'PENDING_APPROVAL',
        'take': '50',
      },
    );
    final data = resp.data as Map<String, dynamic>;
    return (data['items'] as List)
        .map((e) => RequestSummary.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<RequestDetail> getDetail(String requestRef) async {
    final actor = await _actorCode();
    final resp = await _client.get(
      '/api/admin/liquimoly/replenishment/$requestRef',
      queryParameters: {'actorSapUserCode': actor},
    );
    return RequestDetail.fromJson(resp.data as Map<String, dynamic>);
  }

  Future<void> approve(
    String requestRef, {
    Map<String, double>? lineQuantities,
    String? comment,
  }) async {
    final actor = await _actorCode();
    await _client.post(
      '/api/admin/liquimoly/replenishment/$requestRef/approve',
      data: {
        'actor': {'sapUserCode': actor, 'comment': comment},
        'lineQuantities': lineQuantities ?? {},
      },
    );
  }

  Future<void> reject(String requestRef, String reason) async {
    final actor = await _actorCode();
    await _client.post(
      '/api/admin/liquimoly/replenishment/$requestRef/reject',
      data: {
        'actor': {'sapUserCode': actor},
        'rejectionReason': reason,
      },
    );
  }

  Future<List<RequestSummary>> listByStatus(String status) async {
    final actor = await _actorCode();
    final resp = await _client.get(
      '/api/admin/liquimoly/replenishment',
      queryParameters: {
        'actorSapUserCode': actor,
        'status': status,
        'take': '50',
      },
    );
    final data = resp.data as Map<String, dynamic>;
    return (data['items'] as List)
        .map((e) => RequestSummary.fromJson(e as Map<String, dynamic>))
        .toList();
  }
}
