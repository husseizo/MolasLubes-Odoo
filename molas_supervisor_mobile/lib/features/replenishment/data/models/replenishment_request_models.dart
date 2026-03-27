import 'package:molas_supervisor_mobile/features/replenishment/data/models/replenishment_line.dart';

class SapActorContextPayload {
  const SapActorContextPayload({
    required this.sapUserCode,
    this.comment,
  });

  final String sapUserCode;
  final String? comment;

  Map<String, dynamic> toJson() => {
        'sapUserCode': sapUserCode,
        'comment': comment,
      };
}

class LineQuantityOverridePayload {
  const LineQuantityOverridePayload({
    required this.lineId,
    required this.approvedQty,
  });

  final int lineId;
  final double approvedQty;

  Map<String, dynamic> toJson() => {
        'lineId': lineId,
        'approvedQty': approvedQty,
      };
}

class ReplenishmentRequestDetail {
  const ReplenishmentRequestDetail({
    required this.id,
    required this.requestRef,
    required this.sourceProfile,
    required this.targetProfile,
    required this.sourceWarehouse,
    required this.targetWarehouse,
    required this.status,
    required this.requestedBySapUser,
    required this.approvedBySapUser,
    required this.rejectedBySapUser,
    required this.executedBySapUser,
    required this.createdAt,
    required this.submittedAt,
    required this.approvedAt,
    required this.rejectedAt,
    required this.executedAt,
    required this.comments,
    required this.rejectionReason,
    required this.transferRef,
    required this.goodsIssueDocEntry,
    required this.goodsIssueDocNum,
    required this.goodsReceiptDocEntry,
    required this.goodsReceiptDocNum,
    required this.errorMessage,
    required this.lines,
  });

  final int id;
  final String requestRef;
  final String sourceProfile;
  final String targetProfile;
  final String sourceWarehouse;
  final String targetWarehouse;
  final String status;
  final String? requestedBySapUser;
  final String? approvedBySapUser;
  final String? rejectedBySapUser;
  final String? executedBySapUser;
  final DateTime? createdAt;
  final DateTime? submittedAt;
  final DateTime? approvedAt;
  final DateTime? rejectedAt;
  final DateTime? executedAt;
  final String? comments;
  final String? rejectionReason;
  final String? transferRef;
  final int? goodsIssueDocEntry;
  final String? goodsIssueDocNum;
  final int? goodsReceiptDocEntry;
  final String? goodsReceiptDocNum;
  final String? errorMessage;
  final List<ReplenishmentLine> lines;

  factory ReplenishmentRequestDetail.fromJson(Map<String, dynamic> json) {
    final rawLines = (json['lines'] as List<dynamic>? ?? const []);

    return ReplenishmentRequestDetail(
      id: _readInt(json['id']),
      requestRef: json['requestRef']?.toString() ?? '',
      sourceProfile: json['sourceProfile']?.toString() ?? '',
      targetProfile: json['targetProfile']?.toString() ?? '',
      sourceWarehouse: json['sourceWarehouse']?.toString() ?? '',
      targetWarehouse: json['targetWarehouse']?.toString() ?? '',
      status: json['status']?.toString() ?? '',
      requestedBySapUser: json['requestedBySapUser']?.toString(),
      approvedBySapUser: json['approvedBySapUser']?.toString(),
      rejectedBySapUser: json['rejectedBySapUser']?.toString(),
      executedBySapUser: json['executedBySapUser']?.toString(),
      createdAt: _readDateTime(json['createdAt']),
      submittedAt: _readDateTime(json['submittedAt']),
      approvedAt: _readDateTime(json['approvedAt']),
      rejectedAt: _readDateTime(json['rejectedAt']),
      executedAt: _readDateTime(json['executedAt']),
      comments: json['comments']?.toString(),
      rejectionReason: json['rejectionReason']?.toString(),
      transferRef: json['transferRef']?.toString(),
      goodsIssueDocEntry: _readNullableInt(json['goodsIssueDocEntry']),
      goodsIssueDocNum: json['goodsIssueDocNum']?.toString(),
      goodsReceiptDocEntry: _readNullableInt(json['goodsReceiptDocEntry']),
      goodsReceiptDocNum: json['goodsReceiptDocNum']?.toString(),
      errorMessage: json['errorMessage']?.toString(),
      lines: rawLines
          .whereType<Map<String, dynamic>>()
          .map(ReplenishmentLine.fromJson)
          .toList(),
    );
  }
}

class ReplenishmentRequestSummary {
  const ReplenishmentRequestSummary({
    required this.id,
    required this.requestRef,
    required this.sourceProfile,
    required this.targetProfile,
    required this.sourceWarehouse,
    required this.targetWarehouse,
    required this.status,
    required this.requestedBySapUser,
    required this.approvedBySapUser,
    required this.rejectedBySapUser,
    required this.executedBySapUser,
    required this.createdAt,
    required this.submittedAt,
    required this.approvedAt,
    required this.rejectedAt,
    required this.executedAt,
    required this.comments,
    required this.rejectionReason,
    required this.transferRef,
    required this.goodsIssueDocNum,
    required this.goodsReceiptDocNum,
    required this.errorMessage,
    required this.lineCount,
  });

  final int id;
  final String requestRef;
  final String sourceProfile;
  final String targetProfile;
  final String sourceWarehouse;
  final String targetWarehouse;
  final String status;
  final String? requestedBySapUser;
  final String? approvedBySapUser;
  final String? rejectedBySapUser;
  final String? executedBySapUser;
  final DateTime? createdAt;
  final DateTime? submittedAt;
  final DateTime? approvedAt;
  final DateTime? rejectedAt;
  final DateTime? executedAt;
  final String? comments;
  final String? rejectionReason;
  final String? transferRef;
  final String? goodsIssueDocNum;
  final String? goodsReceiptDocNum;
  final String? errorMessage;
  final int lineCount;

  factory ReplenishmentRequestSummary.fromJson(Map<String, dynamic> json) {
    return ReplenishmentRequestSummary(
      id: _readInt(json['id']),
      requestRef: json['requestRef']?.toString() ?? '',
      sourceProfile: json['sourceProfile']?.toString() ?? '',
      targetProfile: json['targetProfile']?.toString() ?? '',
      sourceWarehouse: json['sourceWarehouse']?.toString() ?? '',
      targetWarehouse: json['targetWarehouse']?.toString() ?? '',
      status: json['status']?.toString() ?? '',
      requestedBySapUser: json['requestedBySapUser']?.toString(),
      approvedBySapUser: json['approvedBySapUser']?.toString(),
      rejectedBySapUser: json['rejectedBySapUser']?.toString(),
      executedBySapUser: json['executedBySapUser']?.toString(),
      createdAt: _readDateTime(json['createdAt']),
      submittedAt: _readDateTime(json['submittedAt']),
      approvedAt: _readDateTime(json['approvedAt']),
      rejectedAt: _readDateTime(json['rejectedAt']),
      executedAt: _readDateTime(json['executedAt']),
      comments: json['comments']?.toString(),
      rejectionReason: json['rejectionReason']?.toString(),
      transferRef: json['transferRef']?.toString(),
      goodsIssueDocNum: json['goodsIssueDocNum']?.toString(),
      goodsReceiptDocNum: json['goodsReceiptDocNum']?.toString(),
      errorMessage: json['errorMessage']?.toString(),
      lineCount: _readInt(json['lineCount']),
    );
  }
}

class PagedReplenishmentRequests {
  const PagedReplenishmentRequests({
    required this.items,
    required this.hasMore,
    required this.count,
  });

  final List<ReplenishmentRequestSummary> items;
  final bool hasMore;
  final int count;

  factory PagedReplenishmentRequests.fromJson(Map<String, dynamic> json) {
    final rawItems = (json['items'] as List<dynamic>? ?? const []);

    return PagedReplenishmentRequests(
      items: rawItems
          .whereType<Map<String, dynamic>>()
          .map(ReplenishmentRequestSummary.fromJson)
          .toList(),
      hasMore: json['hasMore'] == true,
      count: _readInt(json['count']),
    );
  }
}

class ReplenishmentActionResult {
  const ReplenishmentActionResult({
    required this.requestRef,
    required this.status,
    required this.approvedBySapUser,
    required this.rejectedBySapUser,
    required this.approvedAt,
    required this.rejectedAt,
    required this.submittedAt,
    required this.rejectionReason,
    required this.lineCount,
  });

  final String requestRef;
  final String status;
  final String? approvedBySapUser;
  final String? rejectedBySapUser;
  final DateTime? approvedAt;
  final DateTime? rejectedAt;
  final DateTime? submittedAt;
  final String? rejectionReason;
  final int? lineCount;

  factory ReplenishmentActionResult.fromJson(Map<String, dynamic> json) {
    return ReplenishmentActionResult(
      requestRef: json['requestRef']?.toString() ?? '',
      status: json['status']?.toString() ?? '',
      approvedBySapUser: json['approvedBySapUser']?.toString(),
      rejectedBySapUser: json['rejectedBySapUser']?.toString(),
      approvedAt: _readDateTime(json['approvedAt']),
      rejectedAt: _readDateTime(json['rejectedAt']),
      submittedAt: _readDateTime(json['submittedAt']),
      rejectionReason: json['rejectionReason']?.toString(),
      lineCount: _readNullableInt(json['lineCount']),
    );
  }
}

int _readInt(dynamic value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value?.toString() ?? '') ?? 0;
}

int? _readNullableInt(dynamic value) {
  if (value == null) return null;
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value.toString());
}

DateTime? _readDateTime(dynamic value) {
  if (value == null) return null;
  return DateTime.tryParse(value.toString());
}
