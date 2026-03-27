import 'package:molas_supervisor_mobile/core/api/api_client.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/recommendation_models.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/replenishment_execution_models.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/replenishment_report_models.dart';
import 'package:molas_supervisor_mobile/features/replenishment/data/models/replenishment_request_models.dart';

class ReplenishmentRepository {
  const ReplenishmentRepository({
    required ApiClient apiClient,
  }) : _apiClient = apiClient;

  final ApiClient _apiClient;

  Future<GenerateDraftResult> generateDraft({
    required String actorSapUserCode,
    required String sourceWarehouse,
    required String targetWarehouse,
    String sourceProfile = 'MolasLubes',
    String targetProfile = 'AutoHub',
    int targetDays = 30,
    String? comment,
  }) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/api/admin/liquimoly/replenishment/generate-draft',
      data: {
        'sourceProfile': sourceProfile,
        'targetProfile': targetProfile,
        'sourceWarehouse': sourceWarehouse,
        'targetWarehouse': targetWarehouse,
        'targetDays': targetDays,
        'actor': SapActorContextPayload(
          sapUserCode: actorSapUserCode,
          comment: comment,
        ).toJson(),
      },
    );

    return GenerateDraftResult.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<ReplenishmentActionResult> submit({
    required String requestRef,
    required String actorSapUserCode,
    String? comment,
  }) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/api/admin/liquimoly/replenishment/$requestRef/submit',
      data: {
        'actor': SapActorContextPayload(
          sapUserCode: actorSapUserCode,
          comment: comment,
        ).toJson(),
      },
    );

    return ReplenishmentActionResult.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<ReplenishmentActionResult> approve({
    required String requestRef,
    required String actorSapUserCode,
    List<LineQuantityOverridePayload> lineQuantities = const [],
    String? comment,
  }) async {
    final payload = <String, dynamic>{
      'actor': SapActorContextPayload(
        sapUserCode: actorSapUserCode,
        comment: comment,
      ).toJson(),
    };

    if (lineQuantities.isNotEmpty) {
      payload['lineQuantities'] =
          lineQuantities.map((item) => item.toJson()).toList();
    }

    final response = await _apiClient.post<Map<String, dynamic>>(
      '/api/admin/liquimoly/replenishment/$requestRef/approve',
      data: payload,
    );

    return ReplenishmentActionResult.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<ReplenishmentActionResult> reject({
    required String requestRef,
    required String actorSapUserCode,
    required String reason,
    String? comment,
  }) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/api/admin/liquimoly/replenishment/$requestRef/reject',
      data: {
        'actor': SapActorContextPayload(
          sapUserCode: actorSapUserCode,
          comment: comment,
        ).toJson(),
        'reason': reason,
      },
    );

    return ReplenishmentActionResult.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<ReplenishmentExecutionResult> execute({
    required String requestRef,
    required String actorSapUserCode,
    String? comment,
  }) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/api/admin/liquimoly/replenishment/$requestRef/execute',
      data: {
        'actor': SapActorContextPayload(
          sapUserCode: actorSapUserCode,
          comment: comment,
        ).toJson(),
      },
    );

    return ReplenishmentExecutionResult.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<ReplenishmentExecutionResult> retry({
    required String requestRef,
    required String actorSapUserCode,
    String? comment,
  }) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/api/admin/liquimoly/replenishment/$requestRef/retry',
      data: {
        'actor': SapActorContextPayload(
          sapUserCode: actorSapUserCode,
          comment: comment,
        ).toJson(),
      },
    );

    return ReplenishmentExecutionResult.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<ReplenishmentRequestDetail> getRequest({
    required String requestRef,
    required String actorSapUserCode,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/api/admin/liquimoly/replenishment/$requestRef',
      queryParameters: {
        'actorSapUserCode': actorSapUserCode,
      },
    );

    return ReplenishmentRequestDetail.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<PagedReplenishmentRequests> listRequests({
    required String actorSapUserCode,
    String? status,
    int skip = 0,
    int take = 50,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/api/admin/liquimoly/replenishment',
      queryParameters: _compactQuery({
        'actorSapUserCode': actorSapUserCode,
        'status': status,
        'skip': skip,
        'take': take,
      }),
    );

    return PagedReplenishmentRequests.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<RecommendationReportResponse> fetchRecommendations({
    required String actorSapUserCode,
    required String sourceWarehouse,
    required String targetWarehouse,
    String sourceProfile = 'MolasLubes',
    String targetProfile = 'AutoHub',
    int targetDays = 30,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/api/admin/liquimoly/reports/recommendations',
      queryParameters: {
        'actorSapUserCode': actorSapUserCode,
        'sourceProfile': sourceProfile,
        'targetProfile': targetProfile,
        'sourceWarehouse': sourceWarehouse,
        'targetWarehouse': targetWarehouse,
        'targetDays': targetDays,
      },
    );

    return RecommendationReportResponse.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<RecommendationReportResponse> fetchDeadStock({
    required String actorSapUserCode,
    required String sourceWarehouse,
    required String targetWarehouse,
    String sourceProfile = 'MolasLubes',
    String targetProfile = 'AutoHub',
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/api/admin/liquimoly/reports/dead-stock',
      queryParameters: {
        'actorSapUserCode': actorSapUserCode,
        'sourceProfile': sourceProfile,
        'targetProfile': targetProfile,
        'sourceWarehouse': sourceWarehouse,
        'targetWarehouse': targetWarehouse,
      },
    );

    return RecommendationReportResponse.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<ExecutionReportResponse> fetchExecutions({
    required String actorSapUserCode,
    int skip = 0,
    int take = 50,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/api/admin/liquimoly/reports/executions',
      queryParameters: {
        'actorSapUserCode': actorSapUserCode,
        'skip': skip,
        'take': take,
      },
    );

    return ExecutionReportResponse.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Future<ApprovalReportResponse> fetchApprovals({
    required String actorSapUserCode,
    int skip = 0,
    int take = 50,
  }) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '/api/admin/liquimoly/reports/approvals',
      queryParameters: {
        'actorSapUserCode': actorSapUserCode,
        'skip': skip,
        'take': take,
      },
    );

    return ApprovalReportResponse.fromJson(
      response.data ?? <String, dynamic>{},
    );
  }

  Map<String, dynamic> _compactQuery(Map<String, dynamic> query) {
    return Map<String, dynamic>.fromEntries(
      query.entries.where((entry) => entry.value != null),
    );
  }
}
