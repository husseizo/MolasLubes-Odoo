import 'package:molas_supervisor_mobile/core/api/api_client.dart';
import 'package:molas_supervisor_mobile/core/storage/secure_storage.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/request_summary.dart';

class ExecutionRepository {
  ExecutionRepository({
    required ApiClient client,
    required SecureStorage storage,
  })  : _client = client,
        _storage = storage;

  final ApiClient _client;
  final SecureStorage _storage;

  Future<String?> _actorCode() => _storage.sapUserCode;

  Future<List<RequestSummary>> listExecuted() async {
    final actor = await _actorCode();
    final resp = await _client.get(
      '/api/admin/liquimoly/replenishment',
      queryParameters: {
        'actorSapUserCode': actor,
        'status': 'EXECUTED',
        'take': '50',
      },
    );
    final data = resp.data as Map<String, dynamic>;
    return (data['items'] as List)
        .map((e) => RequestSummary.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<List<RequestSummary>> listPartialFailed() async {
    final actor = await _actorCode();
    final futures = await Future.wait([
      _listByStatus(actor, 'PARTIAL'),
      _listByStatus(actor, 'FAILED'),
    ]);
    return [...futures[0], ...futures[1]];
  }

  Future<List<RequestSummary>> _listByStatus(String? actor, String status) async {
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

  Future<void> retry(String requestRef) async {
    final actor = await _actorCode();
    await _client.post(
      '/api/admin/liquimoly/replenishment/$requestRef/retry',
      data: {'actorSapUserCode': actor},
    );
  }
}
